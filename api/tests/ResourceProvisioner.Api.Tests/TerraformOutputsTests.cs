namespace ResourceProvisioner.Api.Tests;

using System.Text.Json.Nodes;
using ResourceCatalog;
using Services.Provisioning;
using Xunit;

public class TerraformOutputsTests
{
  private static readonly ResourceManifest Manifest = new()
  {
    Id = "sample",
    DisplayName = "Sample",
    Description = "Sample.",
    CostHint = "Free.",
    Variables = [],
    Outputs =
    [
      new OutputSpec { Name = "ip", Description = "IP." },
      new OutputSpec { Name = "password", Description = "Password.", Sensitive = true },
      new OutputSpec { Name = "token", Description = "Token." },
    ],
  };

  [Fact]
  public void Split_SeparatesSensitiveOutputs_ByManifestOrTerraform()
  {
    const string json = """
      {
        "ip": { "sensitive": false, "type": "string", "value": "192.0.2.1" },
        "password": { "sensitive": false, "type": "string", "value": "p" },
        "token": { "sensitive": true, "type": "string", "value": "t" }
      }
      """;

    var (plain, sensitive) = TerraformOutputs.Split(json, Manifest);

    Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "ip": "192.0.2.1" }"""), plain));
    Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{ "password": "p", "token": "t" }"""), sensitive));
  }

  [Fact]
  public void Split_KeepsStructuredValues()
  {
    const string json = """{ "ip": { "sensitive": false, "type": ["list", "string"], "value": ["a", "b"] } }""";

    var (plain, _) = TerraformOutputs.Split(json, Manifest);

    Assert.Equal(["a", "b"], plain["ip"]!.AsArray().Select(v => v!.GetValue<string>()));
  }

  [Theory]
  [InlineData("not json secret")]
  [InlineData("[1, 2]")]
  public void Split_ThrowsWithoutEchoingText_WhenNotAJsonObject(string text)
  {
    var ex = Assert.Throws<JobFailedException>(() => TerraformOutputs.Split(text, Manifest));

    Assert.DoesNotContain("secret", ex.Message);
  }
}
