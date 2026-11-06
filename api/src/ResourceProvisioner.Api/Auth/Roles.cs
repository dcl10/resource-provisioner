namespace ResourceProvisioner.Api.Auth;

/// <summary>Keycloak realm roles the app checks. Created by <c>keycloak/realm-export.json</c>.</summary>
public static class Roles
{
  /// <summary>Sees every user's conversations and requests, and manages invitations.</summary>
  public const string Admin = "rp-admin";

  /// <summary>Default role every user has.</summary>
  public const string User = "rp-user";
}
