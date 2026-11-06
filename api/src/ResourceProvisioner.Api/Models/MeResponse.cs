namespace ResourceProvisioner.Api.Models;

/// <summary>The signed-in user's ID, email and whether they're an admin.</summary>
/// <param name="UserId">Keycloak user ID (<c>sub</c>).</param>
/// <param name="Email">Email address, if the token has one.</param>
/// <param name="IsAdmin">Whether the user has <c>rp-admin</c>.</param>
public record MeResponse(string UserId, string? Email, bool IsAdmin);
