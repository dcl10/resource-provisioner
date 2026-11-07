namespace ResourceProvisioner.Api.Tests;

using Fixtures;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

/// <summary>How the Keycloak settings shape JWT validation.</summary>
public class JwtBearerOptionsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  [Fact]
  public void HttpKeycloak_IsRefused_OutsideDevelopment_ByDefault()
  {
    // The test host isn't Development and its Keycloak URL is plain HTTP, so JWT validation refuses to use it.
    var ex = Assert.Throws<InvalidOperationException>(() => Bearer(factory.Services));

    Assert.Contains("RequireHttpsMetadata", ex.Message);
  }

  [Fact]
  public void RequireHttpsMetadata_CanBeTurnedOff_ForAPrivateKeycloakAddress()
  {
    using var configured = factory.WithWebHostBuilder(builder =>
      builder.ConfigureAppConfiguration(config =>
        config.AddInMemoryCollection(new Dictionary<string, string?>
        {
          ["Keycloak:RequireHttpsMetadata"] = "false",
          ["Keycloak:AuthServerUrl"] = "http://keycloak:8080",
        })));

    var options = Bearer(configured.Services);

    Assert.False(options.RequireHttpsMetadata);
    Assert.Equal("http://keycloak:8080/realms/resource-provisioner", options.Authority);
  }

  private static JwtBearerOptions Bearer(IServiceProvider services) =>
    services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
}
