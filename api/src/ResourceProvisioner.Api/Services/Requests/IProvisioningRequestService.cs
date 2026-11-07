namespace ResourceProvisioner.Api.Services.Requests;

using System.Text.Json.Nodes;
using Models;

/// <summary>
/// Provisioning requests: proposing them (for the agent's <c>propose_request</c> tool), listing and reading them,
/// the owner's confirm and cancel, and destroy. Users see only their own requests; admins see all of them, and can
/// destroy any of them but confirm and cancel only their own.
/// </summary>
public interface IProvisioningRequestService
{
  /// <summary>
  /// Validates a request and, if it's valid, creates it in <see cref="RequestStatus.AwaitingConfirmation"/>. Checks
  /// the variables against the type's manifest (including its <c>atLeastOneOf</c> rules), and that every ID in a
  /// variable with <c>references</c> is a request of that type <paramref name="owner"/> owns, pending or provisioned,
  /// admins included. Those are recorded as its dependencies. Nothing is provisioned.
  /// </summary>
  /// <param name="owner">The user the request is for; becomes its owner.</param>
  /// <param name="type">The resource type's ID.</param>
  /// <param name="variables">The variables, by name.</param>
  /// <param name="conversationId">The conversation it was proposed in, if any.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The new request, or the errors to fix.</returns>
  Task<ProposeResult> ProposeAsync(
    RequestUser owner,
    string type,
    JsonObject? variables,
    Guid? conversationId,
    CancellationToken cancellationToken = default);

  /// <summary>Lists the user's requests, or every request for an admin, newest first.</summary>
  /// <param name="user">The signed-in user.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The requests.</returns>
  Task<IReadOnlyList<ProvisioningRequestResponse>> ListAsync(
    RequestUser user,
    CancellationToken cancellationToken = default);

  /// <summary>Reads one request: the user's own, or anyone's for an admin.</summary>
  /// <param name="id">Request ID.</param>
  /// <param name="user">The signed-in user.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The request.</returns>
  /// <exception cref="KeyNotFoundException">No such request, or it isn't the user's and they aren't an admin.</exception>
  Task<ProvisioningRequestResponse> GetAsync(Guid id, RequestUser user, CancellationToken cancellationToken = default);

  /// <summary>
  /// The owner's confirm: re-checks the variables and references, enforces the active-request limits, and moves the
  /// request to <see cref="RequestStatus.Queued"/> for the runner. Refused while a request it depends on still awaits
  /// confirmation.
  /// </summary>
  /// <param name="id">Request ID.</param>
  /// <param name="user">The signed-in user; must be the owner.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The updated request.</returns>
  /// <exception cref="KeyNotFoundException">No such request the user can see.</exception>
  /// <exception cref="RequestForbiddenException">An admin confirming someone else's request.</exception>
  /// <exception cref="RequestConflictException">It isn't awaiting confirmation, or changed meanwhile.</exception>
  /// <exception cref="RequestValidationException">Its variables or droplet references are no longer valid.</exception>
  /// <exception cref="RequestLimitException">Confirming would go over a limit.</exception>
  Task<ProvisioningRequestResponse> ConfirmAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Confirms several requests at once (the chat's Confirm all): each must be the user's own and awaiting
  /// confirmation, and still valid. The active-request limits are checked for the whole set, and they're queued in
  /// dependency order, all or none. A request that depends on another still awaiting confirmation can only be
  /// confirmed together with it.
  /// </summary>
  /// <param name="ids">Request IDs, at most <c>ProvisioningRequestService.MaxConfirmAtOnce</c>.</param>
  /// <param name="user">The signed-in user; must own every request.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The updated requests, in the order given.</returns>
  /// <exception cref="KeyNotFoundException">A request the user can't see.</exception>
  /// <exception cref="RequestForbiddenException">An admin confirming someone else's request.</exception>
  /// <exception cref="RequestConflictException">
  /// One isn't awaiting confirmation, changed meanwhile, or depends on an unconfirmed request outside the set.
  /// </exception>
  /// <exception cref="RequestValidationException">One's variables or references are no longer valid.</exception>
  /// <exception cref="RequestLimitException">Confirming them would go over a limit.</exception>
  Task<IReadOnlyList<ProvisioningRequestResponse>> ConfirmManyAsync(
    IReadOnlyList<Guid> ids,
    RequestUser user,
    CancellationToken cancellationToken = default);

  /// <summary>The owner's cancel, before confirming: moves the request to <see cref="RequestStatus.Cancelled"/>.</summary>
  /// <param name="id">Request ID.</param>
  /// <param name="user">The signed-in user; must be the owner.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The updated request.</returns>
  /// <exception cref="KeyNotFoundException">No such request the user can see.</exception>
  /// <exception cref="RequestForbiddenException">An admin cancelling someone else's request.</exception>
  /// <exception cref="RequestConflictException">It isn't awaiting confirmation, or changed meanwhile.</exception>
  Task<ProvisioningRequestResponse> CancelAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// The owner's explicit reveal of a request's sensitive outputs (passwords, connection URIs, keys). Owner only, admins
  /// included: an admin can't read someone else's secrets. Never called on the agent's behalf.
  /// </summary>
  /// <param name="id">Request ID.</param>
  /// <param name="user">The signed-in user; must be the owner.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The sensitive outputs by name; empty if it has none (yet).</returns>
  /// <exception cref="KeyNotFoundException">No such request the user can see.</exception>
  /// <exception cref="RequestForbiddenException">An admin asking for someone else's.</exception>
  Task<JsonObject> GetSensitiveOutputsAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default);

  /// <summary>
  /// Tears down what a request created: moves it to <see cref="RequestStatus.Destroying"/> and onto the runner's
  /// queue. Allowed from <see cref="RequestStatus.Succeeded"/> and <see cref="RequestStatus.Failed"/>. The owner or
  /// an admin may destroy it, so an admin can clean up a resource someone left running.
  /// </summary>
  /// <param name="id">Request ID.</param>
  /// <param name="user">The signed-in user: the owner, or an admin.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The updated request.</returns>
  /// <exception cref="KeyNotFoundException">No such request the user can see.</exception>
  /// <exception cref="RequestConflictException">
  /// It isn't Succeeded or Failed, changed meanwhile, or other requests that depend on it still exist (or may).
  /// </exception>
  Task<ProvisioningRequestResponse> DestroyAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default);
}
