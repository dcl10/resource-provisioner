namespace ResourceProvisioner.Api.Tests;

using System.Text.Json.Nodes;
using ResourceCatalog;
using Xunit;

/// <summary>Checks the real <c>catalog/spaces-bucket</c> manifest accepts and rejects what it should.</summary>
public class SpacesBucketManifestTests
{
  private readonly CatalogService _catalog = new(CatalogLoader.Load(Path.Combine(FindRepoRoot(), "catalog")));

  [Fact]
  public void ValidateVariables_DefaultsRegion_WhenOnlyNameGiven()
  {
    var result = _catalog.ValidateVariables("spaces-bucket", Valid());

    Assert.True(result.IsValid);
    Assert.Equal("nyc3", result.Values["region"]?.GetValue<string>());
  }

  [Theory]
  [InlineData("abc")]
  [InlineData("my-files")]
  [InlineData("0-backups-2026")]
  public void ValidateVariables_AcceptsName_WhenDnsLabel(string name)
  {
    var input = Valid();
    input["name"] = name;

    Assert.True(_catalog.ValidateVariables("spaces-bucket", input).IsValid);
  }

  [Theory]
  [InlineData("ab")]
  [InlineData("My-files")]
  [InlineData("my_files")]
  [InlineData("my.files")]
  [InlineData("-my-files")]
  [InlineData("my-files-")]
  public void ValidateVariables_RejectsName_WhenNotDnsLabel(string name)
  {
    var input = Valid();
    input["name"] = name;

    var result = _catalog.ValidateVariables("spaces-bucket", input);

    Assert.True(result.Errors.ContainsKey("name"));
  }

  [Theory]
  [InlineData(54, true)]
  [InlineData(55, false)]
  public void ValidateVariables_LeavesRoomForSuffix_WhenCheckingNameLength(int length, bool valid)
  {
    var input = Valid();
    input["name"] = new string('a', length);

    var result = _catalog.ValidateVariables("spaces-bucket", input);

    Assert.Equal(valid, result.IsValid);
  }

  [Theory]
  [InlineData("sfo2")]
  [InlineData("blr1")]
  [InlineData("NYC3")]
  public void ValidateVariables_RejectsRegion_WhenNotAllowListed(string region)
  {
    var input = Valid();
    input["region"] = region;

    var result = _catalog.ValidateVariables("spaces-bucket", input);

    Assert.True(result.Errors.ContainsKey("region"));
  }

  [Theory]
  [InlineData("acl")]
  [InlineData("force_destroy")]
  [InlineData("rp_tags")]
  [InlineData("rp_name_suffix")]
  public void ValidateVariables_RejectsField_WhenNotOffered(string field)
  {
    var input = Valid();
    input[field] = "public-read";

    var result = _catalog.ValidateVariables("spaces-bucket", input);

    Assert.Equal(["Unknown field."], result.Errors[field]);
  }

  [Fact]
  public void Find_MarksKeyOutputsSensitive_WhenLoaded()
  {
    var outputs = _catalog.Find("spaces-bucket")!.Outputs.ToDictionary(o => o.Name, o => o.Sensitive);

    Assert.True(outputs["access_key_id"]);
    Assert.True(outputs["secret_access_key"]);
    Assert.False(outputs["bucket_domain_name"]);
  }

  private static JsonObject Valid() => new() { ["name"] = "my-files" };

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
