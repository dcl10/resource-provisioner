namespace ResourceProvisioner.Api.Services.Provisioning;

using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Requests;

/// <summary>
/// The Terraform runner: takes request IDs off the <see cref="IJobDispatcher"/> and runs them one at a time, each in its own
/// scope. When it starts it fails requests a previous run left mid-job and re-queues every <c>Queued</c> one.
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
  /// <summary>The log line added to a request the API stopped in the middle of.</summary>
  public const string InterruptedMessage =
    "Failed: the API stopped while this job was running, so the resource may be partly created or destroyed. " +
    "Destroy the request to clean up.";

  private static readonly RequestStatus[] RunningStatuses = [RequestStatus.Applying, RequestStatus.Destroying];

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
  /// Fails requests left <c>Applying</c> or <c>Destroying</c> (no job runs across a restart), then re-queues the
  /// <c>Queued</c> ones in the order they were confirmed.
  /// </summary>
  private async Task RecoverAsync(CancellationToken cancellationToken)
  {
    try
    {
      using var scope = scopes.CreateScope();
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

      var interrupted = await db.ProvisioningRequests
        .Where(x => RunningStatuses.Contains(x.Status))
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

      // SQLite (tests) can't order by DateTimeOffset, so sort in memory; active requests are capped, so this is small.
      var queued = await db.ProvisioningRequests.AsNoTracking()
        .Where(x => x.Status == RequestStatus.Queued)
        .Select(x => new { x.Id, x.ConfirmedAt })
        .ToListAsync(cancellationToken);
      foreach (var request in queued.OrderBy(x => x.ConfirmedAt))
      {
        await queue.EnqueueAsync(request.Id, cancellationToken);
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
