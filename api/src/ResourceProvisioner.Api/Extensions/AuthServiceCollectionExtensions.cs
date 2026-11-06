namespace ResourceProvisioner.Api.Extensions;

using Auth;
using Config;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

/// <summary>Registers sign-in (Keycloak JWTs) and authorization.</summary>
public static class AuthServiceCollectionExtensions
{
  /// <summary>
  /// Validates Keycloak access tokens (issuer from the realm, audience <c>rp-api</c>), maps realm roles into role
  /// claims, requires a signed-in user on every endpoint by default, and registers <see cref="ICurrentUser"/>.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">App configuration, holding the <c>Keycloak</c> section.</param>
  /// <param name="environment">The host environment; HTTPS metadata is only optional in Development.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddKeycloakAuth(
    this IServiceCollection services,
    IConfiguration configuration,
    IHostEnvironment environment)
  {
    services.AddOptions<KeycloakOptions>()
      .BindConfiguration(KeycloakOptions.SectionName)
      .ValidateDataAnnotations()
      .ValidateOnStart();

    var keycloak = configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>() ?? new KeycloakOptions();

    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
      .AddJwtBearer(options =>
      {
        // Keycloak's discovery document is at {Authority}/.well-known/openid-configuration.
        options.Authority = keycloak.Authority;
        options.Audience = keycloak.Resource;
        options.RequireHttpsMetadata = !environment.IsDevelopment();

        // Keep the JWT claim names (sub, email, realm_access) instead of mapping them to long WS-* URIs.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
          ValidateIssuer = true,
          ValidateAudience = true,
          ValidateLifetime = true,
          ValidateIssuerSigningKey = true,
          ClockSkew = TimeSpan.FromSeconds(30),
          NameClaimType = KeycloakClaims.PreferredUsername,
        };
      });

    services.AddTransient<IClaimsTransformation, RealmRolesClaimsTransformation>();
    services.AddAuthorization(AuthPolicies.Configure);

    services.AddHttpContextAccessor();
    services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

    return services;
  }
}
