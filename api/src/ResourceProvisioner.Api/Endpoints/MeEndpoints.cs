namespace ResourceProvisioner.Api.Endpoints;

using Auth;

/// <summary>Who the caller is, as the API sees them.</summary>
public static class MeEndpoints
{
  /// <summary>The signed-in user's ID, email and whether they're an admin.</summary>
  /// <param name="UserId">Keycloak user ID.</param>
  /// <param name="Email">Email address, if the token has one.</param>
  /// <param name="IsAdmin">Whether the user has <c>rp-admin</c>.</param>
  public record MeResponse(string UserId, string? Email, bool IsAdmin);

  /// <summary>Maps <c>GET /api/me</c>, open to any signed-in user.</summary>
  /// <param name="app">The endpoint route builder.</param>
  /// <returns>The same builder, for chaining.</returns>
  public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
  {
    app.MapGet("/api/me", (ICurrentUser user) => new MeResponse(user.UserId, user.Email, user.IsAdmin));

    return app;
  }
}
