namespace ResourceProvisioner.Api.Services.Requests;

using Models;

/// <summary>A request's Terraform log, for <c>GET /api/requests/{id}/logs</c>.</summary>
public interface IRequestLogService
{
  /// <summary>
  /// Follows a request's log: its status, every line after <paramref name="afterId"/>, then new lines and status
  /// changes as the runner writes them, ending with an end event once no job is queued or running.
  /// </summary>
  /// <param name="id">Request ID.</param>
  /// <param name="user">The signed-in user: the owner, or an admin.</param>
  /// <param name="afterId">Only lines with a higher ID (the SSE <c>Last-Event-ID</c>); 0 for all of them.</param>
  /// <param name="cancellationToken">Cancels the check before the stream starts.</param>
  /// <returns>The events, to enumerate with the caller's own cancellation token.</returns>
  /// <exception cref="KeyNotFoundException">No such request, or it isn't the user's and they aren't an admin.</exception>
  Task<IAsyncEnumerable<RequestLogEvent>> FollowAsync(
    Guid id,
    RequestUser user,
    long afterId,
    CancellationToken cancellationToken = default);
}
