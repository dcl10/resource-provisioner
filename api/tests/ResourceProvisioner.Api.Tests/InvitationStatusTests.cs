namespace ResourceProvisioner.Api.Tests;

using Models;
using Xunit;

public class InvitationStatusTests
{
  private static readonly DateTimeOffset _now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

  [Theory]
  // expiresInHours, revoked, emailVerified, expected
  [InlineData(24, false, false, InvitationStatus.Pending)]
  [InlineData(24, false, true, InvitationStatus.Accepted)]
  [InlineData(-1, false, true, InvitationStatus.Accepted)]
  [InlineData(-1, false, false, InvitationStatus.Expired)]
  [InlineData(0, false, false, InvitationStatus.Expired)]
  [InlineData(24, true, false, InvitationStatus.Revoked)]
  [InlineData(-1, true, false, InvitationStatus.Revoked)]
  [InlineData(24, true, true, InvitationStatus.Revoked)]
  public void StatusAt_DerivesStatus_FromExpiryRevocationAndVerification(
    int expiresInHours,
    bool revoked,
    bool emailVerified,
    InvitationStatus expected)
  {
    var invitation = new Invitation
    {
      Email = "someone@example.com",
      KeycloakUserId = "kc-1",
      InvitedBy = "admin-1",
      SentAt = _now.AddDays(-1),
      ExpiresAt = _now.AddHours(expiresInHours),
      RevokedAt = revoked ? _now.AddHours(-2) : null,
    };

    Assert.Equal(expected, invitation.StatusAt(_now, emailVerified));
  }
}
