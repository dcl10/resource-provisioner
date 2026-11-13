namespace ResourceProvisioner.Api.Services.Requests;

using Models;

/// <summary>
/// The legal moves between <see cref="RequestStatus"/> values. Every status change goes through
/// <see cref="Move"/>, so an illegal one (confirming twice, destroying something still applying) can't happen. A
/// request leaves the machine by being deleted (<see cref="IRequestDeleter"/>): from
/// <see cref="RequestStatus.AwaitingConfirmation"/> when the owner cancels it, and from
/// <see cref="RequestStatus.Destroying"/> once the runner has destroyed it. A destroy that stops before Terraform has
/// touched the request goes back with <see cref="RevertDestroy"/>.
/// </summary>
public static class RequestStateMachine
{
  /// <summary>
  /// Statuses that count towards <c>Limits:MaxActiveRequestsPerUser</c> and <c>Limits:MaxActiveRequestsTotal</c>:
  /// confirmed and not yet finished with, i.e. a resource that exists or is about to. Requests awaiting confirmation
  /// cost nothing, and failed ones don't count either (see docs/architecture.md).
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
    [RequestStatus.AwaitingConfirmation] = [RequestStatus.Queued],
    // Queued -> Failed is the runner's, when something the request depends on failed or went away before it ran.
    [RequestStatus.Queued] = [RequestStatus.Applying, RequestStatus.Failed],
    [RequestStatus.Applying] = [RequestStatus.Succeeded, RequestStatus.Failed],
    [RequestStatus.Succeeded] = [RequestStatus.Destroying],

    // A failed apply can leave resources behind (a droplet created before a later step failed), and a failed
    // destroy certainly can, so a failed request can always be destroyed (again).
    [RequestStatus.Failed] = [RequestStatus.Destroying],
    [RequestStatus.Destroying] = [RequestStatus.DestroyFailed],
    [RequestStatus.DestroyFailed] = [RequestStatus.Destroying],
  };

  /// <summary>
  /// The statuses a request can be destroyed from, and so the ones <see cref="RevertDestroy"/> can return it to.
  /// </summary>
  public static readonly IReadOnlyList<RequestStatus> DestroyableStatuses =
  [
    RequestStatus.Succeeded,
    RequestStatus.Failed,
    RequestStatus.DestroyFailed,
  ];

  /// <summary>Whether a request can move from <paramref name="from"/> to <paramref name="to"/>.</summary>
  /// <param name="from">The current status.</param>
  /// <param name="to">The status to move to.</param>
  /// <returns>True if the move is legal.</returns>
  public static bool CanMove(RequestStatus from, RequestStatus to) =>
    Transitions.TryGetValue(from, out var next) && next.Contains(to);

  /// <summary>The statuses a request can move to from <paramref name="from"/>.</summary>
  /// <param name="from">The current status.</param>
  /// <returns>The legal next statuses.</returns>
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

    if (to == RequestStatus.Destroying)
    {
      request.StatusBeforeDestroy = request.Status;
      request.DestroyStoppedById = null;
    }
    else
    {
      request.StatusBeforeDestroy = null;
    }

    request.Status = to;
    request.UpdatedAt = now;
    if (to == RequestStatus.Queued)
    {
      request.ConfirmedAt = now;
    }
  }

  /// <summary>
  /// Moves a <see cref="RequestStatus.Destroying"/> request back to the status it had before
  /// (<see cref="ProvisioningRequest.StatusBeforeDestroy"/>), because its destroy stopped before Terraform touched it:
  /// something that depends on it failed to destroy. Nothing was done to it, so it really is still in that status. A
  /// request moved to Destroying before that was recorded goes to <see cref="RequestStatus.Failed"/>, which claims
  /// nothing about what exists and can be destroyed again.
  /// </summary>
  /// <param name="request">The request to change.</param>
  /// <param name="stoppedById">The request whose failed destroy stopped this one's.</param>
  /// <param name="now">The current time.</param>
  /// <exception cref="RequestConflictException">The request isn't Destroying.</exception>
  public static void RevertDestroy(ProvisioningRequest request, Guid stoppedById, DateTimeOffset now)
  {
    if (request.Status != RequestStatus.Destroying)
    {
      throw new RequestConflictException(
        $"Request {request.Id} is {request.Status}, so there's no destroy to stop.");
    }

    request.Status = request.StatusBeforeDestroy is { } before && DestroyableStatuses.Contains(before)
      ? before
      : RequestStatus.Failed;
    request.StatusBeforeDestroy = null;
    request.DestroyStoppedById = stoppedById;
    request.UpdatedAt = now;
  }
}
