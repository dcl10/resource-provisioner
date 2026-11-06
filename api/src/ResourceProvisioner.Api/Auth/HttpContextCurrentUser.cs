namespace ResourceProvisioner.Api.Auth;

/// <summary><see cref="ICurrentUser"/> read from the current request's authenticated user.</summary>
/// <param name="httpContextAccessor">Access to the current request.</param>
public class HttpContextCurrentUser(IHttpContextAccessor httpContextAccessor) : ICurrentUser
{
  /// <inheritdoc />
  public string UserId =>
    User?.FindFirst(KeycloakClaims.Subject)?.Value
    ?? throw new InvalidOperationException("There is no signed-in user with a 'sub' claim on this request.");

  /// <inheritdoc />
  public string? Email => User?.FindFirst(KeycloakClaims.Email)?.Value;

  /// <inheritdoc />
  public bool IsAdmin => User?.IsInRole(Roles.Admin) ?? false;

  private System.Security.Claims.ClaimsPrincipal? User =>
    httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } user ? user : null;
}
