namespace ResourceProvisioner.Api.Services.Invitations;

using Keycloak;
using Models;

/// <summary>Invites colleagues by email, and lists, resends and revokes invitations. Admin-only.</summary>
public interface IInvitationService
{
  /// <summary>Lists every invitation, newest first, with its status as of now.</summary>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The invitations.</returns>
  /// <exception cref="KeycloakAdminException">Keycloak couldn't be asked which emails are verified.</exception>
  Task<IReadOnlyList<InvitationResponse>> ListAsync(CancellationToken cancellationToken = default);

  /// <summary>
  /// Creates a Keycloak user for the email and sends Keycloak's action email, in which the colleague verifies their
  /// email and sets a password. If the email can't be sent, the user is deleted again.
  /// </summary>
  /// <param name="email">The colleague's email address.</param>
  /// <param name="invitedBy">Keycloak user ID of the admin sending the invitation.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The new invitation.</returns>
  /// <exception cref="InvitationConflictException">A Keycloak user with this email already exists.</exception>
  /// <exception cref="KeycloakAdminException">Keycloak failed to create the user or send the email.</exception>
  Task<InvitationResponse> InviteAsync(string email, string invitedBy, CancellationToken cancellationToken = default);

  /// <summary>Sends a fresh action email, with a new expiry. Only for pending or expired invitations.</summary>
  /// <param name="id">Invitation ID.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The updated invitation.</returns>
  /// <exception cref="KeyNotFoundException">No invitation has this ID.</exception>
  /// <exception cref="InvitationConflictException">The invitation was accepted or revoked.</exception>
  /// <exception cref="KeycloakAdminException">Keycloak failed to send the email.</exception>
  Task<InvitationResponse> ResendAsync(Guid id, CancellationToken cancellationToken = default);

  /// <summary>
  /// Revokes a pending or expired invitation by disabling its Keycloak user, so the emailed link stops working.
  /// </summary>
  /// <param name="id">Invitation ID.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The updated invitation.</returns>
  /// <exception cref="KeyNotFoundException">No invitation has this ID.</exception>
  /// <exception cref="InvitationConflictException">The invitation was accepted or is already revoked.</exception>
  /// <exception cref="KeycloakAdminException">Keycloak failed to disable the user.</exception>
  Task<InvitationResponse> RevokeAsync(Guid id, CancellationToken cancellationToken = default);
}
