namespace ResourceProvisioner.Api.Models;

/// <summary>
/// What destroying a request would do (<c>GET /api/requests/{id}/destroy-plan</c>): it destroys the request and
/// everything that depends on it, directly or not, and cancels (deletes) the proposals among them. The destroy itself
/// (<c>POST /api/requests/{id}/destroy</c>) works the plan out again, so it may differ if something changed meanwhile.
/// </summary>
/// <param name="CanDestroy">Whether a destroy would be accepted now.</param>
/// <param name="Reason">Why not, in the words the destroy's 409 would use; null when it can.</param>
/// <param name="Destroy">
/// What would be destroyed, in teardown order: each request before anything it depends on, so the request itself is
/// last.
/// </param>
/// <param name="Cancel">The proposals (<c>AwaitingConfirmation</c>) that depend on it, which would be deleted.</param>
/// <param name="WaitFor">
/// What depends on it and is queued, applying or being destroyed: a destroy is refused until these have finished.
/// </param>
public record DestroyPlanResponse(
  bool CanDestroy,
  string? Reason,
  IReadOnlyList<RequestLink> Destroy,
  IReadOnlyList<RequestLink> Cancel,
  IReadOnlyList<RequestLink> WaitFor);
