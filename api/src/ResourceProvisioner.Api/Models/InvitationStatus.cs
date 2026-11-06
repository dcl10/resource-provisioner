namespace ResourceProvisioner.Api.Models;

using System.Text.Json.Serialization;

/// <summary>
/// Where an invitation has got to. Never stored: derived each time it's read, see <see cref="Invitation.StatusAt"/>.
/// Serialized by name (<c>"Pending"</c>), not number.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<InvitationStatus>))]
public enum InvitationStatus
{
  /// <summary>Sent, and the link hasn't been used or run out yet.</summary>
  Pending,

  /// <summary>The invited user verified their email through the link, so they can sign in.</summary>
  Accepted,

  /// <summary>Not accepted, and the link has run out. Resending sends a fresh one.</summary>
  Expired,

  /// <summary>Withdrawn by an admin. The Keycloak user is disabled, so the link no longer works.</summary>
  Revoked,
}
