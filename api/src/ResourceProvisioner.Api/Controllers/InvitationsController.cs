namespace ResourceProvisioner.Api.Controllers;

using Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services.Invitations;
using Services.Keycloak;

/// <summary>
/// Invitations: the only way a colleague gets an account. Admins only. Errors come back as problem details with a
/// <c>detail</c> an admin can read: 404 for an unknown invitation, 409 when the email already has an account or the
/// invitation was accepted or revoked, 502 when Keycloak fails.
/// </summary>
/// <param name="invitations">The invitation service.</param>
/// <param name="currentUser">The signed-in admin.</param>
[ApiController]
[Route("api/invitations")]
[Authorize(Policy = AuthPolicies.AdminOnly)]
public class InvitationsController(IInvitationService invitations, ICurrentUser currentUser) : ControllerBase
{
  /// <summary>Lists every invitation, newest first, with its current status.</summary>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The invitations.</returns>
  [HttpGet]
  public Task<ActionResult<IReadOnlyList<InvitationResponse>>> List(CancellationToken cancellationToken) =>
    Handle(() => invitations.ListAsync(cancellationToken));

  /// <summary>Invites a colleague: creates their Keycloak user and emails them a link to set up their account.</summary>
  /// <param name="request">The colleague's email.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>201 with the invitation.</returns>
  [HttpPost]
  public async Task<ActionResult<InvitationResponse>> Invite(
    InviteRequest request,
    CancellationToken cancellationToken)
  {
    var result = await Handle(() => invitations.InviteAsync(request.Email, currentUser.UserId, cancellationToken));

    return result.Value is { } invitation ? Created($"api/invitations/{invitation.Id}", invitation) : result;
  }

  /// <summary>Sends a pending or expired invitation again, with a fresh link and expiry.</summary>
  /// <param name="id">Invitation ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The updated invitation.</returns>
  [HttpPost("{id:guid}/resend")]
  public Task<ActionResult<InvitationResponse>> Resend(Guid id, CancellationToken cancellationToken) =>
    Handle(() => invitations.ResendAsync(id, cancellationToken));

  /// <summary>Revokes a pending or expired invitation by disabling the invited user in Keycloak.</summary>
  /// <param name="id">Invitation ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The updated invitation.</returns>
  [HttpPost("{id:guid}/revoke")]
  public Task<ActionResult<InvitationResponse>> Revoke(Guid id, CancellationToken cancellationToken) =>
    Handle(() => invitations.RevokeAsync(id, cancellationToken));

  /// <summary>Maps the service's exceptions to problem details.</summary>
  private async Task<ActionResult<T>> Handle<T>(Func<Task<T>> action)
  {
    try
    {
      return await action();
    }
    catch (KeyNotFoundException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
    catch (InvitationConflictException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
    catch (KeycloakAdminException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
    }
  }
}
