namespace ResourceProvisioner.Api.Services.Keycloak;

/// <summary>The parts of a Keycloak user the app reads.</summary>
/// <param name="Id">Keycloak user ID.</param>
/// <param name="Email">Email address, if set.</param>
/// <param name="EmailVerified">Whether the email has been verified.</param>
/// <param name="Enabled">Whether the user can sign in.</param>
public record KeycloakUser(string Id, string? Email, bool EmailVerified, bool Enabled);
