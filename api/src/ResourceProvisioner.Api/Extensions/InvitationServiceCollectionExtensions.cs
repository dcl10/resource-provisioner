namespace ResourceProvisioner.Api.Extensions;

using Config;
using Duende.AccessTokenManagement;
using Keycloak.AuthServices.Sdk.Kiota;
using Services.Invitations;
using Services.Keycloak;

/// <summary>Registers invitations and the Keycloak admin API client they use.</summary>
public static class InvitationServiceCollectionExtensions
{
  /// <summary>
  /// Registers <see cref="IInvitationService"/>, its options, and <see cref="IKeycloakUserAdmin"/> over the Kiota
  /// admin client. The client gets its tokens with client credentials as <c>rp-api</c>, through
  /// Duende.AccessTokenManagement, which caches them until they expire. Call after <c>AddKeycloakAuth</c>, which
  /// registers <see cref="KeycloakOptions"/>.
  /// </summary>
  /// <param name="services">The service collection.</param>
  /// <param name="configuration">App configuration, holding the <c>Keycloak</c> section.</param>
  /// <returns>The same service collection, for chaining.</returns>
  public static IServiceCollection AddInvitations(this IServiceCollection services, IConfiguration configuration)
  {
    services.AddOptions<InvitationOptions>()
      .BindConfiguration(InvitationOptions.SectionName)
      .ValidateDataAnnotations()
      .ValidateOnStart();

    var keycloak = configuration.GetSection(KeycloakOptions.SectionName).Get<KeycloakOptions>() ?? new KeycloakOptions();
    var clientName = ClientCredentialsClientName.Parse(keycloak.Resource);

    services
      .AddKeycloakAdminHttpClient(o =>
      {
        o.AuthServerUrl = keycloak.AuthServerUrl;
        o.Realm = keycloak.Realm;
        o.Resource = keycloak.Resource;
      })
      .AddClientCredentialsTokenHandler(clientName);

    services.AddDistributedMemoryCache();
    services
      .AddClientCredentialsTokenManagement()
      .AddClient(clientName, o =>
      {
        // Runs on first use, not at startup, so the app starts without a secret; KeycloakUserAdmin reports it missing.
        o.ClientId = ClientId.Parse(keycloak.Resource);
        o.ClientSecret = string.IsNullOrWhiteSpace(keycloak.Secret) ? null : ClientSecret.Parse(keycloak.Secret);
        o.TokenEndpoint = new Uri($"{keycloak.Authority}/protocol/openid-connect/token");
      });

    services.AddScoped<IKeycloakUserAdmin, KeycloakUserAdmin>();
    services.AddScoped<IInvitationService, InvitationService>();
    services.AddSingleton(TimeProvider.System);

    return services;
  }
}
