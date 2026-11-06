namespace ResourceProvisioner.Api.Tests;

using System.Security.Claims;
using Auth;
using Xunit;

public class RealmRolesClaimsTransformationTests
{
  private readonly RealmRolesClaimsTransformation _transformation = new();

  [Fact]
  public async Task TransformAsync_AddsRoleClaims_FromRealmAccess()
  {
    var principal = Authenticated("""{"roles":["rp-user","rp-admin"]}""");

    var result = await _transformation.TransformAsync(principal);

    Assert.True(result.IsInRole("rp-user"));
    Assert.True(result.IsInRole("rp-admin"));
  }

  [Fact]
  public async Task TransformAsync_DoesNotDuplicateRoles_WhenRunTwice()
  {
    var principal = Authenticated("""{"roles":["rp-user"]}""");

    await _transformation.TransformAsync(principal);
    var result = await _transformation.TransformAsync(principal);

    Assert.Single(result.FindAll(ClaimTypes.Role));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("not json")]
  [InlineData("""["rp-admin"]""")]
  [InlineData("""{"roles":"rp-admin"}""")]
  [InlineData("""{"roles":[1, null, ""]}""")]
  public async Task TransformAsync_AddsNoRoles_WhenRealmAccessIsMissingOrMalformed(string? realmAccess)
  {
    var principal = Authenticated(realmAccess);

    var result = await _transformation.TransformAsync(principal);

    Assert.Empty(result.FindAll(ClaimTypes.Role));
  }

  [Fact]
  public async Task TransformAsync_LeavesAnonymousUserAlone()
  {
    var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(KeycloakClaims.RealmAccess, """{"roles":["rp-admin"]}""")]));

    var result = await _transformation.TransformAsync(principal);

    Assert.False(result.IsInRole("rp-admin"));
  }

  private static ClaimsPrincipal Authenticated(string? realmAccess)
  {
    List<Claim> claims = [new(KeycloakClaims.Subject, "user-1")];
    if (realmAccess is not null)
    {
      claims.Add(new Claim(KeycloakClaims.RealmAccess, realmAccess));
    }

    return new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Test"));
  }
}
