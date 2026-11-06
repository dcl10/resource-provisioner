namespace ResourceProvisioner.Api.Tests;

using System.Net;
using System.Text.Json.Nodes;
using Fixtures;
using Xunit;

public class CatalogControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  [Fact]
  public async Task GetCatalog_ReturnsUnauthorized_WithoutCredentials()
  {
    var response = await factory.CreateAnonymousClient().GetAsync("/api/catalog");

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task GetCatalog_ReturnsTypesOrderedById_WhenSignedIn()
  {
    var response = await factory.CreateUserClient().GetAsync("/api/catalog");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var types = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsArray();
    Assert.Equal(["test-firewall", "test-server"], types.Select(t => t!["id"]!.GetValue<string>()));
  }

  [Fact]
  public async Task GetCatalog_SerializesManifestKeywords_AsInResourceJson()
  {
    var json = await factory.CreateUserClient().GetStringAsync("/api/catalog");

    var server = JsonNode.Parse(json)!.AsArray().Single(t => t!["id"]!.GetValue<string>() == "test-server")!;
    var size = server["variables"]!.AsArray().Single(v => v!["name"]!.GetValue<string>() == "size")!;
    Assert.Equal("string", size["type"]!.GetValue<string>());
    Assert.Equal(["small", "medium"], size["allowedValues"]!.AsArray().Select(v => v!.GetValue<string>()));
    Assert.Equal("small", size["default"]!.GetValue<string>());
    Assert.False(size.AsObject().ContainsKey("pattern"));

    var rootPassword = server["outputs"]!.AsArray().Single(o => o!["name"]!.GetValue<string>() == "root_password")!;
    Assert.True(rootPassword["sensitive"]!.GetValue<bool>());
  }

  [Fact]
  public async Task GetCatalog_SerializesNestedItemSpecs_ForListOfObjects()
  {
    var json = await factory.CreateUserClient().GetStringAsync("/api/catalog");

    var firewall = JsonNode.Parse(json)!.AsArray().Single(t => t!["id"]!.GetValue<string>() == "test-firewall")!;
    var properties = firewall["variables"]![0]!["items"]!["properties"]!.AsArray();
    Assert.Equal(["protocol", "port", "sources"], properties.Select(p => p!["name"]!.GetValue<string>()));
  }
}
