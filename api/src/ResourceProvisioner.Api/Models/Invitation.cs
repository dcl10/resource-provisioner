namespace ResourceProvisioner.Api.Models;

/// <summary>
/// An invitation to use the app, sent to one email address. The Keycloak user is created when the invitation is sent,
/// and Keycloak's action email lets them verify their email and set a password.
/// </summary>
public class Invitation
{
  /// <summary>Unique identifier.</summary>
  public Guid Id { get; set; }

  /// <summary>The address the invitation was sent to, lower-cased. Also the Keycloak username.</summary>
  public required string Email { get; set; }

  /// <summary>ID of the Keycloak user created for the invitation.</summary>
  public required string KeycloakUserId { get; set; }

  /// <summary>Keycloak user ID (<c>sub</c>) of the admin who sent the invitation.</summary>
  public required string InvitedBy { get; set; }

  /// <summary>When the invitation email was last sent.</summary>
  public DateTimeOffset SentAt { get; set; }

  /// <summary>When the link in the last email stops working.</summary>
  public DateTimeOffset ExpiresAt { get; set; }

  /// <summary>When an admin revoked the invitation, if they have.</summary>
  public DateTimeOffset? RevokedAt { get; set; }

  /// <summary>
  /// The status as of a point in time. Revoked wins; otherwise a verified email means accepted, and an unaccepted
  /// invitation past its expiry has expired.
  /// </summary>
  /// <param name="now">The time to check expiry against.</param>
  /// <param name="emailVerified">Whether Keycloak reports the invited user's email as verified.</param>
  /// <returns>The status to show.</returns>
  public InvitationStatus StatusAt(DateTimeOffset now, bool emailVerified) =>
    RevokedAt is not null ? InvitationStatus.Revoked
    : emailVerified ? InvitationStatus.Accepted
    : ExpiresAt <= now ? InvitationStatus.Expired
    : InvitationStatus.Pending;
}
