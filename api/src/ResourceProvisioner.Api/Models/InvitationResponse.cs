namespace ResourceProvisioner.Api.Models;

/// <summary>An invitation as the admin Users page shows it.</summary>
/// <param name="Id">Invitation ID, used by the resend and revoke endpoints.</param>
/// <param name="Email">The address the invitation was sent to.</param>
/// <param name="Status">The derived status.</param>
/// <param name="InvitedBy">Keycloak user ID of the admin who sent it.</param>
/// <param name="SentAt">When the email was last sent.</param>
/// <param name="ExpiresAt">When the last email's link stops working.</param>
/// <param name="RevokedAt">When it was revoked, if it was.</param>
public record InvitationResponse(
  Guid Id,
  string Email,
  InvitationStatus Status,
  string InvitedBy,
  DateTimeOffset SentAt,
  DateTimeOffset ExpiresAt,
  DateTimeOffset? RevokedAt);
