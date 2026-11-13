namespace ResourceProvisioner.Api.Services.Requests;

using System.Runtime.CompilerServices;
using Config;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Models;

/// <summary>
/// <see cref="IRequestLogService"/> that polls the database every <c>Runner:LogPollInterval</c>. The runner writes
/// lines from its own scope, so polling is what lets any API request follow it.
/// </summary>
/// <param name="db">The app database.</param>
/// <param name="options">The poll interval.</param>
/// <param name="time">The clock the poll waits on.</param>
public class RequestLogService(AppDbContext db, IOptions<RunnerOptions> options, TimeProvider time)
  : IRequestLogService
{
  /// <summary>The most lines read per query, so a long log is sent in pages.</summary>
  public const int PageSize = 500;

  private static readonly RequestStatus[] JobStatuses =
    [RequestStatus.Queued, RequestStatus.Applying, RequestStatus.Destroying];

  /// <inheritdoc />
  public async Task<IAsyncEnumerable<RequestLogEvent>> FollowAsync(
    Guid id,
    RequestUser user,
    long afterId,
    CancellationToken cancellationToken = default)
  {
    var ownerId = await db.ProvisioningRequests.AsNoTracking()
      .Where(x => x.Id == id)
      .Select(x => x.OwnerId)
      .SingleOrDefaultAsync(cancellationToken);
    if (ownerId is null || (ownerId != user.UserId && !user.IsAdmin))
    {
      throw new KeyNotFoundException($"Request {id} doesn't exist.");
    }

    return Follow(id, afterId, CancellationToken.None);
  }

  private async IAsyncEnumerable<RequestLogEvent> Follow(
    Guid id,
    long afterId,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    RequestStatus? sent = null;
    while (true)
    {
      // Status before lines: a line written just before the job finished is then read on this pass or the next.
      var found = await db.ProvisioningRequests.AsNoTracking()
        .Where(x => x.Id == id)
        .Select(x => (RequestStatus?)x.Status)
        .SingleOrDefaultAsync(cancellationToken);

      // Destroyed, so deleted with its log: there's nothing more to send.
      if (found is not { } status)
      {
        yield return new RequestLogEvent(RequestLogEventKind.Deleted, null, null, null);
        yield break;
      }

      if (status != sent)
      {
        sent = status;
        yield return new RequestLogEvent(RequestLogEventKind.Status, null, null, status);
      }

      var lines = await db.JobLogLines.AsNoTracking()
        .Where(x => x.RequestId == id && x.Id > afterId)
        .OrderBy(x => x.Id)
        .Take(PageSize)
        .ToListAsync(cancellationToken);
      foreach (var line in lines)
      {
        afterId = line.Id;
        yield return new RequestLogEvent(
          RequestLogEventKind.Log,
          line.Id,
          new JobLogLineResponse(line.Id, line.At, line.Stream, line.Text),
          null);
      }

      if (lines.Count == PageSize)
      {
        continue;
      }

      if (!JobStatuses.Contains(status))
      {
        yield return new RequestLogEvent(RequestLogEventKind.End, null, null, status);
        yield break;
      }

      await Task.Delay(options.Value.LogPollInterval, time, cancellationToken);
    }
  }
}
