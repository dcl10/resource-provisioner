namespace ResourceProvisioner.Api.Tests;

using System.Text.Json.Nodes;
using ResourceCatalog;
using Xunit;

/// <summary>Checks the real <c>catalog/droplet</c> manifest accepts and rejects what it should.</summary>
public class DropletManifestTests
{
  private const string Ed25519Key =
    "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGq4nKe0bJ8yQ0bWx6xQ9b7r3Z4mQ0kq3yS5b7a1c2d3 alice@laptop";

  private readonly CatalogService _catalog = new(CatalogLoader.Load(Path.Combine(FindRepoRoot(), "catalog")));

  [Fact]
  public void ValidateVariables_AppliesCheapDefaults_WhenOnlyRequiredGiven()
  {
    var input = new JsonObject { ["name"] = "web-1", ["ssh_public_key"] = Ed25519Key };

    var result = _catalog.ValidateVariables("droplet", input);

    Assert.True(result.IsValid);
    Assert.Equal("s-1vcpu-512mb-10gb", result.Values["size"]?.GetValue<string>());
    Assert.Equal("nyc3", result.Values["region"]?.GetValue<string>());
    Assert.Equal("ubuntu-24-04-x64", result.Values["image"]?.GetValue<string>());
  }

  [Theory]
  [InlineData("ssh-rsa AAAAB3NzaC1yc2EAAAADAQABAAABAQC7")]
  [InlineData("ecdsa-sha2-nistp256 AAAAE2VjZHNhLXNoYTItbmlzdHAyNTYAAAAIbmlzdHAyNTY= bob")]
  public void ValidateVariables_AcceptsPublicKey_ForSupportedKeyTypes(string key)
  {
    var input = new JsonObject { ["name"] = "web-1", ["ssh_public_key"] = key };

    Assert.True(_catalog.ValidateVariables("droplet", input).IsValid);
  }

  [Theory]
  [InlineData("-----BEGIN OPENSSH PRIVATE KEY-----")]
  [InlineData("ssh-ed25519 AAAA\nssh-ed25519 BBBB")]
  [InlineData("ssh-dss AAAAB3NzaC1kc3M=")]
  [InlineData("AAAAC3NzaC1lZDI1NTE5AAAAIGq4")]
  public void ValidateVariables_RejectsSshKey_WhenNotOnePublicKeyLine(string key)
  {
    var input = new JsonObject { ["name"] = "web-1", ["ssh_public_key"] = key };

    var result = _catalog.ValidateVariables("droplet", input);

    Assert.True(result.Errors.ContainsKey("ssh_public_key"));
  }

  [Theory]
  [InlineData("size", "s-2vcpu-4gb")]
  [InlineData("region", "ams2")]
  [InlineData("image", "ubuntu-20-04-x64")]
  public void ValidateVariables_RejectsValue_WhenNotAllowListed(string field, string value)
  {
    var input = new JsonObject { ["name"] = "web-1", ["ssh_public_key"] = Ed25519Key, [field] = value };

    var result = _catalog.ValidateVariables("droplet", input);

    Assert.True(result.Errors.ContainsKey(field));
  }

  [Fact]
  public void ValidateVariables_RejectsRunnerTags_WhenSetByRequest()
  {
    var input = new JsonObject
    {
      ["name"] = "web-1", ["ssh_public_key"] = Ed25519Key, ["rp_tags"] = new JsonArray("rp-owner-someone-else"),
    };

    var result = _catalog.ValidateVariables("droplet", input);

    Assert.Equal(["Unknown field."], result.Errors["rp_tags"]);
  }

  [Fact]
  public void ValidateVariables_RejectsNameSuffix_WhenSetByRequest()
  {
    var input = new JsonObject { ["name"] = "web-1", ["ssh_public_key"] = Ed25519Key, ["rp_name_suffix"] = "abc" };

    var result = _catalog.ValidateVariables("droplet", input);

    Assert.Equal(["Unknown field."], result.Errors["rp_name_suffix"]);
  }

  [Theory]
  [InlineData(54, true)]
  [InlineData(55, false)]
  public void ValidateVariables_LeavesRoomForSuffix_WhenCheckingNameLength(int length, bool valid)
  {
    var input = new JsonObject { ["name"] = new string('a', length), ["ssh_public_key"] = Ed25519Key };

    var result = _catalog.ValidateVariables("droplet", input);

    Assert.Equal(valid, result.IsValid);
  }

  private static string FindRepoRoot()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !(Directory.Exists(Path.Combine(dir.FullName, "catalog")) &&
                                Directory.Exists(Path.Combine(dir.FullName, "api"))))
    {
      dir = dir.Parent;
    }

    return dir?.FullName ?? throw new InvalidOperationException("Couldn't find the repo root above the test output.");
  }
}
