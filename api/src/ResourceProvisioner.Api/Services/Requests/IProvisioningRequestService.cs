namespace ResourceProvisioner.Api.Services.Requests;

using System.Text.Json.Nodes;
using Models;

/// <summary>
/// Provisioning requests: proposing them (for the agent's <c>propose_request</c> tool), listing and reading them,
/// and the owner's confirm and cancel. Users see only their own requests; admins see all of them but can act only on
/// their own.
/// </summary>
public interface IProvisioningRequestService
{
  /// <summary>
  /// Validates a request and, if it's valid, creates it in <see cref="RequestStatus.AwaitingConfirmation"/>. Checks
  /// the variables against the type's manifest (including its <c>atLeastOneOf</c> rules), and that every ID in a
  /// droplet-reference variable is a <c>droplet</c> request <paramref name="owner"/> owns that has
  /// <see cref="RequestStatus.Succeeded"/>, admins included. Nothing is provisioned.
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
  /// The owner's confirm: re-checks the variables and droplet references, enforces the active-request limits, and
  /// moves the request to <see cref="RequestStatus.Queued"/> for the runner.
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
}
