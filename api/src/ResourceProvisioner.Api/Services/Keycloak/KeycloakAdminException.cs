namespace ResourceProvisioner.Api.Services.Keycloak;

/// <summary>A Keycloak admin API call failed: Keycloak was unreachable, refused the call, or isn't configured.</summary>
/// <param name="message">What failed.</param>
/// <param name="innerException">The underlying error, if any.</param>
public class KeycloakAdminException(string message, Exception? innerException = null)
  : Exception(message, innerException);
