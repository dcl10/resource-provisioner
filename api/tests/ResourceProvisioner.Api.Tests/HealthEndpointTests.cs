namespace ResourceProvisioner.Api.Tests;

using System.Net;
using Fixtures;
using Xunit;

public class HealthEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  [Fact]
  public async Task GetHealth_ReturnsHealthy_WithoutAuthentication()
  {
    var response = await factory.CreateClient().GetAsync("/health");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
  }
}
