namespace ResourceProvisioner.Api.Auth;

/// <summary>Claim names in Keycloak access tokens. Inbound claims are not renamed, so these are the JWT names.</summary>
public static class KeycloakClaims
{
  /// <summary>Stable user ID.</summary>
  public const string Subject = "sub";

  /// <summary>Email address.</summary>
  public const string Email = "email";

  /// <summary>Username, which is the email address for invited users.</summary>
  public const string PreferredUsername = "preferred_username";

  /// <summary>JSON object <c>{ "roles": [...] }</c> holding the user's realm roles.</summary>
  public const string RealmAccess = "realm_access";
}
