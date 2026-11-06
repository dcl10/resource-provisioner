namespace ResourceProvisioner.Api.Services.Requests;

using Models;

/// <summary>
/// The legal moves between <see cref="RequestStatus"/> values. Every status change goes through
/// <see cref="Move"/>, so an illegal one (confirming twice, cancelling something already applying) can't happen.
/// </summary>
public static class RequestStateMachine
{
  /// <summary>
  /// Statuses that count towards <c>Limits:MaxActiveRequestsPerUser</c> and <c>Limits:MaxActiveRequestsTotal</c>:
  /// confirmed and not yet finished with, i.e. a resource that exists or is about to. Awaiting confirmation,
  /// cancelled and destroyed requests cost nothing; failed ones don't count either (see docs/architecture.md).
  /// </summary>
  public static readonly IReadOnlyList<RequestStatus> ActiveStatuses =
  [
    RequestStatus.Queued,
    RequestStatus.Applying,
    RequestStatus.Succeeded,
    RequestStatus.Destroying,
  ];

  private static readonly Dictionary<RequestStatus, RequestStatus[]> Transitions = new()
  {
    [RequestStatus.AwaitingConfirmation] = [RequestStatus.Queued, RequestStatus.Cancelled],
    [RequestStatus.Queued] = [RequestStatus.Applying],
    [RequestStatus.Applying] = [RequestStatus.Succeeded, RequestStatus.Failed],
    [RequestStatus.Succeeded] = [RequestStatus.Destroying],

    // A failed apply can leave resources behind (a droplet created before a later step failed), and a failed
    // destroy certainly can, so a failed request can always be destroyed (again).
    [RequestStatus.Failed] = [RequestStatus.Destroying],
    [RequestStatus.Destroying] = [RequestStatus.Destroyed, RequestStatus.Failed],
    [RequestStatus.Destroyed] = [],
    [RequestStatus.Cancelled] = [],
  };

  /// <summary>Whether a request can move from <paramref name="from"/> to <paramref name="to"/>.</summary>
  /// <param name="from">The current status.</param>
  /// <param name="to">The status to move to.</param>
  /// <returns>True if the move is legal.</returns>
  public static bool CanMove(RequestStatus from, RequestStatus to) =>
    Transitions.TryGetValue(from, out var next) && next.Contains(to);

  /// <summary>The statuses a request can move to from <paramref name="from"/>.</summary>
  /// <param name="from">The current status.</param>
  /// <returns>The legal next statuses; empty for a final status.</returns>
  public static IReadOnlyList<RequestStatus> NextStatuses(RequestStatus from) =>
    Transitions.TryGetValue(from, out var next) ? next : [];

  /// <summary>Moves a request to a new status and records when.</summary>
  /// <param name="request">The request to change.</param>
  /// <param name="to">The new status.</param>
  /// <param name="now">The current time.</param>
  /// <exception cref="RequestConflictException">The move isn't legal from the request's current status.</exception>
  public static void Move(ProvisioningRequest request, RequestStatus to, DateTimeOffset now)
  {
    if (!CanMove(request.Status, to))
    {
      throw new RequestConflictException(
        $"Request {request.Id} is {request.Status}, so it can't move to {to}.");
    }

    request.Status = to;
    request.UpdatedAt = now;
    if (to == RequestStatus.Queued)
    {
      request.ConfirmedAt = now;
    }
  }
}
