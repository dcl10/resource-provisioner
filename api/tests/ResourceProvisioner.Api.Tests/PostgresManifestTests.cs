namespace ResourceProvisioner.Api.Tests;

using System.Text.Json.Nodes;
using ResourceCatalog;
using Xunit;

/// <summary>Checks the real <c>catalog/postgres</c> manifest accepts and rejects what it should.</summary>
public class PostgresManifestTests
{
  private const string RequestId = "3f2b8c1e-5d4a-4e6f-9a7b-1c2d3e4f5a6b";

  private readonly CatalogService _catalog = new(CatalogLoader.Load(Path.Combine(FindRepoRoot(), "catalog")));

  private ResourceManifest Manifest => _catalog.Find("postgres")!;

  [Fact]
  public void ValidateVariables_AppliesCheapDefaults_WhenOnlyNameAndSourceGiven()
  {
    var result = _catalog.ValidateVariables("postgres", Valid());

    Assert.True(result.IsValid);
    Assert.Equal("db-s-1vcpu-1gb", result.Values["size"]?.GetValue<string>());
    Assert.Equal("nyc3", result.Values["region"]?.GetValue<string>());
    Assert.Equal("18", result.Values["postgres_version"]?.GetValue<string>());
    Assert.Equal("app", result.Values["database_name"]?.GetValue<string>());
    Assert.Equal("app_user", result.Values["user_name"]?.GetValue<string>());
    Assert.Empty(result.Values["allowed_droplet_request_ids"]!.AsArray());
  }

  [Fact]
  public void ValidateVariables_AcceptsDatabaseAndUser_WhenNamesValid()
  {
    var input = Valid();
    input["database_name"] = "shop";
    input["user_name"] = "shop_app";

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
    var input = Valid();
    input[field] = value;

    var result = _catalog.ValidateVariables("postgres", input);

    Assert.True(result.Errors.ContainsKey(field));
  }

  [Theory]
  [InlineData(34, true)]
  [InlineData(35, false)]
  public void ValidateVariables_LeavesRoomForSuffix_WhenCheckingNameLength(int length, bool valid)
  {
    var input = Valid();
    input["name"] = new string('a', length);

    var result = _catalog.ValidateVariables("postgres", input);

    Assert.Equal(valid, result.IsValid);
  }

  [Theory]
  [InlineData("size", "db-s-2vcpu-4gb")]
  [InlineData("region", "ams2")]
  [InlineData("postgres_version", "14")]
  [InlineData("postgres_version", "15")]
  public void ValidateVariables_RejectsValue_WhenNotAllowListed(string field, string value)
  {
    var input = Valid();
    input[field] = value;

    var result = _catalog.ValidateVariables("postgres", input);

    Assert.True(result.Errors.ContainsKey(field));
  }

  [Fact]
  public void ValidateVariables_AcceptsDropletRequestId_WhenLowercaseGuid()
  {
    var input = new JsonObject { ["name"] = "app-db", ["allowed_droplet_request_ids"] = new JsonArray(RequestId) };

    Assert.True(_catalog.ValidateVariables("postgres", input).IsValid);
  }

  [Theory]
  [InlineData("3F2B8C1E-5D4A-4E6F-9A7B-1C2D3E4F5A6B")]
  [InlineData("rp-3f2b8c1e-5d4a-4e6f-9a7b-1c2d3e4f5a6b")]
  [InlineData("resource-provisioner")]
  [InlineData("web-1")]
  [InlineData("123456789")]
  public void ValidateVariables_RejectsDropletRequestId_WhenNotARequestId(string value)
  {
    var input = new JsonObject { ["name"] = "app-db", ["allowed_droplet_request_ids"] = new JsonArray(value) };

    var result = _catalog.ValidateVariables("postgres", input);

    Assert.True(result.Errors.ContainsKey("allowed_droplet_request_ids[0]"));
  }

  [Fact]
  public void ValidateVariables_RejectsDropletRequestIds_WhenMoreThanTen()
  {
    var ids = new JsonArray(Enumerable.Range(0, 11).Select(i => (JsonNode?)$"3f2b8c1e-5d4a-4e6f-9a7b-1c2d3e4f5a{i:x2}").ToArray());
    var input = new JsonObject { ["name"] = "app-db", ["allowed_droplet_request_ids"] = ids };

    var result = _catalog.ValidateVariables("postgres", input);

    Assert.True(result.Errors.ContainsKey("allowed_droplet_request_ids"));
  }

  [Theory]
  [InlineData("203.0.113.7/32")]
  [InlineData("192.168.0.0/16")]
  [InlineData("10.0.0.0/8")]
  public void ValidateVariables_AcceptsIpAddress_WhenIpv4CidrFromSlash8(string address)
  {
    Assert.True(_catalog.ValidateVariables("postgres", Valid(address)).IsValid);
  }

  [Theory]
  [InlineData("0.0.0.0/0")]
  [InlineData("0.0.0.0/1")]
  [InlineData("10.0.0.0/7")]
  [InlineData("::/0")]
  [InlineData("2001:db8::/32")]
  [InlineData("203.0.113.7")]
  [InlineData("203.0.113.7/33")]
  [InlineData("256.0.0.1/32")]
  public void ValidateVariables_RejectsIpAddress_WhenNotRestrictedIpv4Cidr(string address)
  {
    var result = _catalog.ValidateVariables("postgres", Valid(address));

    Assert.True(result.Errors.ContainsKey("allowed_ip_addresses[0]"));
  }

  [Theory]
  [InlineData("rp_tags")]
  [InlineData("rp_name_suffix")]
  public void ValidateVariables_RejectsRunnerVariable_WhenSetByRequest(string field)
  {
    var input = Valid();
    input[field] = "rp-owner-someone-else";

    var result = _catalog.ValidateVariables("postgres", input);

    Assert.Equal(["Unknown field."], result.Errors[field]);
  }

  [Fact]
  public void Manifest_MarksOutputsSensitive_OnlyWhenTheyHoldAPassword()
  {
    var sensitive = Manifest.Outputs.Where(o => o.Sensitive).Select(o => o.Name).Order();

    Assert.Equal(["admin_password", "admin_uri", "password", "uri"], sensitive);
  }

  private static JsonObject Valid(string address = "203.0.113.7/32") =>
    new() { ["name"] = "app-db", ["allowed_ip_addresses"] = new JsonArray(address) };

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
