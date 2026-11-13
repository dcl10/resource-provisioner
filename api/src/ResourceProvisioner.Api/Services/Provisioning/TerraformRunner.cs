namespace ResourceProvisioner.Api.Services.Provisioning;

using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Requests;

/// <summary>
/// The Terraform runner: takes request IDs off the <see cref="IJobDispatcher"/> and runs them one at a time, each in
/// its own scope. When it starts it fails requests a previous run left mid-apply and re-queues every <c>Queued</c> and
/// <c>Destroying</c> one.
/// </summary>
/// <param name="queue">The job queue.</param>
/// <param name="scopes">Creates a scope per job.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">Logs job errors.</param>
public partial class TerraformRunner(
  IJobDispatcher queue,
  IServiceScopeFactory scopes,
  TimeProvider time,
  ILogger<TerraformRunner> logger) : BackgroundService
{
  /// <summary>The log line added to a request the API stopped in the middle of applying.</summary>
  public const string InterruptedMessage =
    "Failed: the API stopped while this request was being applied, so the resource may be partly created. " +
    "Destroy the request to clean up.";

  private static readonly RequestStatus[] PendingStatuses = [RequestStatus.Queued, RequestStatus.Destroying];

  /// <inheritdoc />
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    await RecoverAsync(stoppingToken);

    await foreach (var requestId in queue.ReadAllAsync(stoppingToken))
    {
      try
      {
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITerraformJobProcessor>().RunAsync(requestId, stoppingToken);
        await RequeueDependents(scope.ServiceProvider.GetRequiredService<AppDbContext>(), requestId, stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        return;
      }
      catch (Exception ex)
      {
        // One bad job mustn't stop the runner.
        LogJobFailed(logger, ex, requestId);
      }
    }
  }

  /// <summary>
  /// Queues the queued requests that depend on one whose job just ended, so they run once it has succeeded, or fail
  /// if it didn't. Duplicates in the queue are harmless.
  /// </summary>
  private async Task RequeueDependents(AppDbContext db, Guid requestId, CancellationToken cancellationToken)
  {
    var dependentIds = await db.RequestDependencies.AsNoTracking()
      .Where(d => d.DependsOnId == requestId)
      .Select(d => d.RequestId)
      .ToListAsync(cancellationToken);
    var waiting = await db.ProvisioningRequests.AsNoTracking()
      .Where(x => dependentIds.Contains(x.Id) && x.Status == RequestStatus.Queued)
      .Select(x => x.Id)
      .ToListAsync(cancellationToken);
    foreach (var id in waiting)
    {
      await queue.EnqueueAsync(id, cancellationToken);
    }
  }

  /// <summary>
  /// Fails requests left <c>Applying</c> (no job runs across a restart, and a half-done apply isn't retried), then
  /// re-queues the <c>Queued</c> and <c>Destroying</c> ones, oldest change first. A <c>Destroying</c> request may
  /// have been waiting or half-done; either way running <c>terraform destroy</c> again is safe.
  /// </summary>
  private async Task RecoverAsync(CancellationToken cancellationToken)
  {
    try
    {
      using var scope = scopes.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

      var interrupted = await db.ProvisioningRequests
        .Where(x => x.Status == RequestStatus.Applying)
        .ToListAsync(cancellationToken);
      foreach (var request in interrupted)
      {
        var now = time.GetUtcNow();
        RequestStateMachine.Move(request, RequestStatus.Failed, now);
        db.JobLogLines.Add(new JobLogLine
        {
          RequestId = request.Id, At = now, Stream = LogSource.System, Text = InterruptedMessage,
        });
      }

      await db.SaveChangesAsync(cancellationToken);

      var pending = await db.ProvisioningRequests.AsNoTracking()
        .Where(x => PendingStatuses.Contains(x.Status))
        .OrderBy(x => x.UpdatedAt)
        .Select(x => x.Id)
        .ToListAsync(cancellationToken);
      foreach (var id in pending)
      {
        await queue.EnqueueAsync(id, cancellationToken);
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // Confirms still queue new jobs; only recovery is skipped.
      LogRecoveryFailed(logger, ex);
    }
  }

  [LoggerMessage(Level = LogLevel.Error, Message = "The job for request {RequestId} failed")]
  private static partial void LogJobFailed(ILogger logger, Exception exception, Guid requestId);

  [LoggerMessage(Level = LogLevel.Error,
    Message = "The Terraform runner couldn't recover interrupted and queued requests at startup")]
  private static partial void LogRecoveryFailed(ILogger logger, Exception exception);
}
