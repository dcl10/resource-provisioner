namespace ResourceProvisioner.Api.Auth;

using Microsoft.AspNetCore.Authorization;

/// <summary>Authorization policies. Endpoints without one still need a signed-in user (the fallback policy).</summary>
public static class AuthPolicies
{
  /// <summary>Policy name for endpoints only <see cref="Roles.Admin"/> can call.</summary>
  public const string AdminOnly = nameof(AdminOnly);

  /// <summary>
  /// Requires a signed-in user on every endpoint that doesn't say otherwise, so a new endpoint can't be left open by
  /// mistake. Endpoints that must be anonymous (only <c>/health</c>) call <c>AllowAnonymous()</c>.
  /// </summary>
  /// <param name="options">The authorization options to configure.</param>
  public static void Configure(AuthorizationOptions options)
  {
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();

    options.AddPolicy(AdminOnly, policy => policy.RequireAuthenticatedUser().RequireRole(Roles.Admin));
  }
}
