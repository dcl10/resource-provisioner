namespace ResourceProvisioner.Api.Services.Keycloak;

/// <summary>Keycloak refused to create a user because the username or email is already taken.</summary>
/// <param name="email">The email that was taken.</param>
/// <param name="innerException">The underlying error, if any.</param>
public class KeycloakUserExistsException(string email, Exception? innerException = null)
  : KeycloakAdminException($"A user with the email {email} already exists.", innerException);
