namespace ResourceProvisioner.Api.Services.Requests;

using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Provisioning;

/// <summary>
/// <see cref="IRequestDeleter"/> on the app database. The request's sensitive outputs, job log lines, own
/// dependencies and project go with it by the database's cascades; what depends on it is unlinked first; its Terraform
/// state, in the same database, is deleted in the same transaction. The only trace left is a line in the API's log.
/// </summary>
/// <param name="db">The app database.</param>
/// <param name="stateStore">Where Terraform keeps state.</param>
/// <param name="logger">Records each delete.</param>
public partial class RequestDeleter(
  AppDbContext db,
  ITerraformStateStore stateStore,
  ILogger<RequestDeleter> logger) : IRequestDeleter
{
  /// <inheritdoc />
  public async Task DeleteCancelledAsync(Guid requestId, CancellationToken cancellationToken)
  {
    await DeleteAsync(requestId, RequestStatus.AwaitingConfirmation, deleteState: false, cancellationToken);
    LogCancelled(logger, requestId);
  }

  /// <inheritdoc />
  public async Task DeleteDestroyedAsync(Guid requestId, CancellationToken cancellationToken)
  {
    await DeleteAsync(requestId, RequestStatus.Destroying, deleteState: true, cancellationToken);
    LogDestroyed(logger, requestId);
  }

  private async Task DeleteAsync(
    Guid requestId,
    RequestStatus expectedStatus,
    bool deleteState,
    CancellationToken cancellationToken)
  {
    await using var ownTransaction = db.Database.CurrentTransaction is null
      ? await db.Database.BeginTransactionAsync(cancellationToken)
      : null;

    // What depends on it, directly or by being in the project it backs. Only proposals may: anything confirmed may
    // exist, and the runner doesn't destroy a request while something that may exist depends on it, so finding one
    // here means something went wrong; stop rather than orphan it.
    var projectId = await db.Projects
      .Where(p => p.ProjectRequestId == requestId)
      .Select(p => (Guid?)p.Id)
      .SingleOrDefaultAsync(cancellationToken);
    var dependentIds = db.RequestDependencies.Where(d => d.DependsOnId == requestId).Select(d => d.RequestId);
    var live = await db.ProvisioningRequests
      .Where(r => (dependentIds.Contains(r.Id) || (projectId != null && r.ProjectId == projectId)) &&
                  r.Status != RequestStatus.AwaitingConfirmation)
      .CountAsync(cancellationToken);
    if (live > 0)
    {
      throw new RequestConflictException(
        $"Request {requestId} can't be deleted: {live} request(s) that depend on it may still exist.");
    }

    // Proposals that depended on it stay, without the link; confirming them fails the reference or project check.
    await db.RequestDependencies.Where(d => d.DependsOnId == requestId).ExecuteDeleteAsync(cancellationToken);
    if (projectId is not null)
    {
      await db.ProvisioningRequests
        .Where(r => r.ProjectId == projectId)
        .ExecuteUpdateAsync(s => s.SetProperty(r => r.ProjectId, (Guid?)null), cancellationToken);
    }

    if (deleteState)
    {
      await stateStore.DeleteStateAsync(requestId, cancellationToken);
    }

    // Last, and only in the status the caller expects: if a confirm or another job changed it meanwhile, nothing is
    // deleted. The cascades take its sensitive outputs, log lines, own dependencies and project.
    var deleted = await db.ProvisioningRequests
      .Where(r => r.Id == requestId && r.Status == expectedStatus)
      .ExecuteDeleteAsync(cancellationToken);
    if (deleted == 0)
    {
      throw new RequestConflictException(
        "A request changed while this was being done. Reload it and try again.");
    }

    if (ownTransaction is not null)
    {
      await ownTransaction.CommitAsync(cancellationToken);
    }
  }

  [LoggerMessage(Level = LogLevel.Information, Message = "Cancelled and deleted request {RequestId}")]
  private static partial void LogCancelled(ILogger logger, Guid requestId);

  [LoggerMessage(Level = LogLevel.Information, Message = "Destroyed and deleted request {RequestId}")]
  private static partial void LogDestroyed(ILogger logger, Guid requestId);
}
