namespace ResourceProvisioner.Api.Config;

using System.ComponentModel.DataAnnotations;

/// <summary>Where the API finds Keycloak, and the <c>rp-api</c> client it signs in as.</summary>
public class KeycloakOptions
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "Keycloak";

  /// <summary>Keycloak base URL, e.g. <c>http://localhost:8080</c>.</summary>
  [Required]
  public string AuthServerUrl { get; init; } = "";

  /// <summary>Realm name.</summary>
  [Required]
  public string Realm { get; init; } = "";

  /// <summary>The API's client ID. Access tokens must carry it in <c>aud</c>.</summary>
  [Required]
  public string Resource { get; init; } = "";

  /// <summary>
  /// The <c>rp-api</c> client secret, for the admin API (invites, #8). Secret: user-secrets or the environment only.
  /// Token validation doesn't need it.
  /// </summary>
  public string Secret { get; init; } = "";

  /// <summary>
  /// Whether Keycloak's discovery document must be fetched over HTTPS. Unset, it's required everywhere but in
  /// Development. Set it to <c>false</c> when the API reaches Keycloak over a private network by plain HTTP, as in Docker
  /// Compose (<c>http://keycloak:8080</c>), even though users reach Keycloak over HTTPS.
  /// </summary>
  public bool? RequireHttpsMetadata { get; init; }

  /// <summary>Issuer and OIDC discovery base, derived from <see cref="AuthServerUrl"/> and <see cref="Realm"/>.</summary>
  public string Authority => $"{AuthServerUrl.TrimEnd('/')}/realms/{Realm}";
}
