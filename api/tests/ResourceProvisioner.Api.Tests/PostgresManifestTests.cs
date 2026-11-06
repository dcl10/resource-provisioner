namespace ResourceProvisioner.Api.Tests;

using System.Text.Json.Nodes;
using ResourceCatalog;
using Xunit;

/// <summary>Checks the real <c>catalog/postgres</c> manifest accepts and rejects what it should.</summary>
public class PostgresManifestTests
{
  private readonly CatalogService _catalog = new(CatalogLoader.Load(Path.Combine(FindRepoRoot(), "catalog")));

  private ResourceManifest Manifest => _catalog.Find("postgres")!;

  [Fact]
  public void ValidateVariables_AppliesCheapDefaults_WhenOnlyNameGiven()
  {
    var result = _catalog.ValidateVariables("postgres", new JsonObject { ["name"] = "app-db" });

    Assert.True(result.IsValid);
    Assert.Equal("db-s-1vcpu-1gb", result.Values["size"]?.GetValue<string>());
    Assert.Equal("nyc3", result.Values["region"]?.GetValue<string>());
    Assert.Equal("18", result.Values["postgres_version"]?.GetValue<string>());
    Assert.False(result.Values.ContainsKey("database_name"));
    Assert.False(result.Values.ContainsKey("user_name"));
  }

  [Fact]
  public void ValidateVariables_AcceptsDatabaseAndUser_WhenNamesValid()
  {
    var input = new JsonObject { ["name"] = "app-db", ["database_name"] = "shop", ["user_name"] = "shop_app" };

    Assert.True(_catalog.ValidateVariables("postgres", input).IsValid);
  }

  [Theory]
  [InlineData("name", "db")]
  [InlineData("name", "app-db-")]
  [InlineData("name", "App-db")]
  [InlineData("database_name", "defaultdb")]
  [InlineData("database_name", "postgres")]
  [InlineData("database_name", "template1")]
  [InlineData("database_name", "shop-db")]
  [InlineData("user_name", "doadmin")]
  [InlineData("user_name", "pg_monitor")]
  [InlineData("user_name", "1user")]
  public void ValidateVariables_RejectsName_WhenInvalidOrReserved(string field, string value)
  {
    var input = new JsonObject { ["name"] = "app-db", [field] = value };

    var result = _catalog.ValidateVariables("postgres", input);

    Assert.True(result.Errors.ContainsKey(field));
  }

  [Theory]
  [InlineData("size", "db-s-2vcpu-4gb")]
  [InlineData("region", "ams2")]
  [InlineData("postgres_version", "14")]
  [InlineData("postgres_version", "15")]
  public void ValidateVariables_RejectsValue_WhenNotAllowListed(string field, string value)
  {
    var input = new JsonObject { ["name"] = "app-db", [field] = value };

    var result = _catalog.ValidateVariables("postgres", input);

    Assert.True(result.Errors.ContainsKey(field));
  }

  [Fact]
  public void ValidateVariables_RejectsRunnerTags_WhenSetByRequest()
  {
    var input = new JsonObject { ["name"] = "app-db", ["rp_tags"] = new JsonArray("rp-owner-someone-else") };

    var result = _catalog.ValidateVariables("postgres", input);

    Assert.Equal(["Unknown field."], result.Errors["rp_tags"]);
  }

  [Fact]
  public void Manifest_MarksOutputsSensitive_OnlyWhenTheyHoldThePassword()
  {
    var sensitive = Manifest.Outputs.Where(o => o.Sensitive).Select(o => o.Name).Order();

    Assert.Equal(["password", "uri"], sensitive);
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
