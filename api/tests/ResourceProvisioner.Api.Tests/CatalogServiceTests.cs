namespace ResourceProvisioner.Api.Tests;

using System.Text.Json.Nodes;
using Fixtures;
using ResourceCatalog;
using Xunit;

public class CatalogServiceTests
{
  private readonly CatalogService _catalog = new(CatalogLoader.Load(ApiFactory.FixtureCatalogRoot));

  [Fact]
  public void Find_ReturnsManifest_ForKnownType()
  {
    Assert.Equal("Test server", _catalog.Find("test-server")?.DisplayName);
  }

  [Fact]
  public void Find_ReturnsNull_ForUnknownType()
  {
    Assert.Null(_catalog.Find("nope"));
  }

  [Fact]
  public void GetModuleDirectory_ReturnsTypeFolder_ForKnownType()
  {
    Assert.Equal(Path.Combine(ApiFactory.FixtureCatalogRoot, "test-server"),
      _catalog.GetModuleDirectory("test-server"));
  }

  [Fact]
  public void GetModuleDirectory_Throws_ForUnknownType()
  {
    Assert.Throws<KeyNotFoundException>(() => _catalog.GetModuleDirectory("nope"));
  }

  [Fact]
  public void ValidateVariables_AppliesDefaults_WhenOnlyRequiredGiven()
  {
    var result = _catalog.ValidateVariables("test-server", Parse("""{ "name": "web-1" }"""));

    Assert.True(result.IsValid);
    Assert.True(JsonNode.DeepEquals(
      Parse("""{ "name": "web-1", "size": "small", "node_count": 1, "backups": false, "tags": [] }"""),
      result.Values));
  }

  [Fact]
  public void ValidateVariables_KeepsGivenValues_WhenAllValid()
  {
    var input = Parse("""
      { "name": "web-1", "size": "medium", "node_count": 3, "backups": true, "tags": ["a", "b-2"],
        "admin_password": "s3cret" }
      """);

    var result = _catalog.ValidateVariables("test-server", input);

    Assert.True(result.IsValid);
    Assert.True(JsonNode.DeepEquals(input, result.Values));
  }

  [Fact]
  public void ValidateVariables_TreatsNullAsMissing_WhenVariableHasDefault()
  {
    var result = _catalog.ValidateVariables("test-server", Parse("""{ "name": "web-1", "size": null }"""));

    Assert.True(result.IsValid);
    Assert.Equal("small", result.Values["size"]!.GetValue<string>());
  }

  [Fact]
  public void ValidateVariables_LeavesOutOptionalVariable_WhenItHasNoDefault()
  {
    var result = _catalog.ValidateVariables("test-server", Parse("""{ "name": "web-1" }"""));

    Assert.False(result.Values.ContainsKey("admin_password"));
  }

  [Fact]
  public void ValidateVariables_ReportsRequired_WhenRequiredVariableMissing()
  {
    var result = _catalog.ValidateVariables("test-server", null);

    Assert.False(result.IsValid);
    Assert.Equal(["Required."], result.Errors["name"]);
  }

  [Theory]
  [InlineData("""{ "name": 5 }""", "name", "Must be a string.")]
  [InlineData("""{ "name": "Web_1" }""", "name", "Must match the pattern ^[a-z][a-z0-9-]*$.")]
  [InlineData("""{ "name": "abcdefghijklmnopqrstu" }""", "name", "Must be at most 20 characters.")]
  [InlineData("""{ "name": "w", "size": "huge" }""", "size", "Must be one of: small, medium.")]
  [InlineData("""{ "name": "w", "node_count": 0 }""", "node_count", "Must be at least 1.")]
  [InlineData("""{ "name": "w", "node_count": 4 }""", "node_count", "Must be at most 3.")]
  [InlineData("""{ "name": "w", "node_count": 1.5 }""", "node_count", "Must be a whole number.")]
  [InlineData("""{ "name": "w", "node_count": "2" }""", "node_count", "Must be a whole number.")]
  [InlineData("""{ "name": "w", "backups": "yes" }""", "backups", "Must be true or false.")]
  [InlineData("""{ "name": "w", "tags": "a" }""", "tags", "Must be a list.")]
  [InlineData("""{ "name": "w", "tags": ["a", "b", "c"] }""", "tags", "Must have at most 2 items.")]
  [InlineData("""{ "name": "w", "tags": ["a", "B"] }""", "tags[1]", "Must match the pattern ^[a-z0-9-]+$.")]
  [InlineData("""{ "name": "w", "tags": ["a", null] }""", "tags[1]", "Must not be null.")]
  [InlineData("""{ "name": "w", "region": "lon1" }""", "region", "Unknown field.")]
  public void ValidateVariables_ReportsFieldError_WhenValueIsInvalid(string json, string path, string message)
  {
    var result = _catalog.ValidateVariables("test-server", Parse(json));

    Assert.False(result.IsValid);
    Assert.Equal([message], result.Errors[path]);
  }

  [Fact]
  public void ValidateVariables_ReportsEveryError_WhenSeveralFieldsAreInvalid()
  {
    var result = _catalog.ValidateVariables("test-server", Parse("""{ "size": "huge", "backups": 1 }"""));

    Assert.Equal(["backups", "name", "size"], result.Errors.Keys.Order());
  }

  [Fact]
  public void ValidateVariables_ReportsTypeError_ForUnknownType()
  {
    var result = _catalog.ValidateVariables("nope", Parse("{}"));

    Assert.Equal(["Unknown resource type 'nope'."], result.Errors[CatalogService.TypeErrorKey]);
  }

  [Fact]
  public void ValidateVariables_AppliesNestedDefaults_ForListOfObjects()
  {
    var result = _catalog.ValidateVariables("test-firewall", Parse("""
      { "rules": [{ "protocol": "tcp", "port": 22, "sources": ["10.0.0.0/8"] }, { "protocol": "udp", "port": 53 }] }
      """));

    Assert.True(result.IsValid);
    Assert.True(JsonNode.DeepEquals(Parse("""
      { "rules": [
        { "protocol": "tcp", "port": 22, "sources": ["10.0.0.0/8"] },
        { "protocol": "udp", "port": 53, "sources": ["0.0.0.0/0"] }
      ] }
      """), result.Values));
  }

  [Theory]
  [InlineData("""{ "rules": [] }""", "rules", "Must have at least 1 items.")]
  [InlineData("""{ "rules": ["tcp"] }""", "rules[0]", "Must be an object.")]
  [InlineData("""{ "rules": [{ "port": 22 }] }""", "rules[0].protocol", "Required.")]
  [InlineData("""{ "rules": [{ "protocol": "icmp", "port": 22 }] }""", "rules[0].protocol",
    "Must be one of: tcp, udp.")]
  [InlineData("""{ "rules": [{ "protocol": "tcp", "port": 70000 }] }""", "rules[0].port", "Must be at most 65535.")]
  [InlineData("""{ "rules": [{ "protocol": "tcp", "port": 22, "action": "allow" }] }""", "rules[0].action",
    "Unknown field.")]
  public void ValidateVariables_ReportsNestedPath_WhenListOfObjectsIsInvalid(string json, string path, string message)
  {
    var result = _catalog.ValidateVariables("test-firewall", Parse(json));

    Assert.Equal([message], result.Errors[path]);
  }

  [Fact]
  public void ValidateVariables_DoesNotShareDefaults_BetweenResults()
  {
    var first = _catalog.ValidateVariables("test-server", Parse("""{ "name": "a" }"""));
    first.Values["tags"]!.AsArray().Add("mutated");

    var second = _catalog.ValidateVariables("test-server", Parse("""{ "name": "b" }"""));

    Assert.Empty(second.Values["tags"]!.AsArray());
  }

  [Fact]
  public void ValidateVariables_ReportsOnEveryVariable_WhenNoneOfAtLeastOneOfIsSet()
  {
    var result = _catalog.ValidateVariables("test-database", Parse("""{ "name": "db" }"""));

    const string message = "Give at least one of allowed_droplet_request_ids, allowed_ip_addresses.";
    Assert.Equal([message], result.Errors["allowed_droplet_request_ids"]);
    Assert.Equal([message], result.Errors["allowed_ip_addresses"]);
  }

  [Theory]
  [InlineData("""{ "name": "db", "allowed_ip_addresses": ["203.0.113.7/32"] }""")]
  [InlineData("""{ "name": "db", "allowed_droplet_request_ids": ["3f2b8c1e-5d4a-4e6f-9a7b-1c2d3e4f5a6b"] }""")]
  public void ValidateVariables_Passes_WhenOneOfAtLeastOneOfIsSet(string json)
  {
    Assert.True(_catalog.ValidateVariables("test-database", Parse(json)).IsValid);
  }

  [Fact]
  public void ValidateVariables_SkipsAtLeastOneOf_WhenAVariableInItHasErrors()
  {
    var result = _catalog.ValidateVariables(
      "test-database",
      Parse("""{ "name": "db", "allowed_droplet_request_ids": ["NOT-A-GUID"] }"""));

    Assert.Equal(["allowed_droplet_request_ids[0]"], result.Errors.Keys);
  }

  private static JsonObject Parse(string json) => JsonNode.Parse(json)!.AsObject();
}
