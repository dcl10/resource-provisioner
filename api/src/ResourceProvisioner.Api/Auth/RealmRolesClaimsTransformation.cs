namespace ResourceProvisioner.Api.Auth;

using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

/// <summary>
/// Copies the realm roles from Keycloak's nested <c>realm_access.roles</c> claim into standard role claims, so
/// <c>RequireRole</c> and <c>IsInRole</c> work. Client roles (<c>resource_access</c>) aren't used by this app.
/// </summary>
public class RealmRolesClaimsTransformation : IClaimsTransformation
{
  /// <inheritdoc />
  public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
  {
    if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
    {
      return Task.FromResult(principal);
    }

    foreach (var role in ReadRealmRoles(identity.FindFirst(KeycloakClaims.RealmAccess)?.Value))
    {
      // The transformation can run more than once per request, so don't add a role twice.
      if (!identity.HasClaim(identity.RoleClaimType, role))
      {
        identity.AddClaim(new Claim(identity.RoleClaimType, role));
      }
    }

    return Task.FromResult(principal);
  }

  private static List<string> ReadRealmRoles(string? realmAccess)
  {
    if (string.IsNullOrEmpty(realmAccess))
    {
      return [];
    }

    try
    {
      using var document = JsonDocument.Parse(realmAccess);

      if (document.RootElement.ValueKind != JsonValueKind.Object ||
          !document.RootElement.TryGetProperty("roles", out var roles) ||
          roles.ValueKind != JsonValueKind.Array)
      {
        return [];
      }

      return roles.EnumerateArray()
        .Where(role => role.ValueKind == JsonValueKind.String)
        .Select(role => role.GetString()!)
        .Where(role => role.Length > 0)
        .ToList();
    }
    catch (JsonException)
    {
      return [];
    }
  }
}
