namespace ResourceProvisioner.Api.Tests;

using System.Text.Json.Nodes;
using ResourceCatalog;
using Xunit;

/// <summary>Checks the real <c>catalog/firewall</c> manifest accepts and rejects what it should.</summary>
public class FirewallManifestTests
{
  private const string RequestId = "3f2b8c1e-5d4a-4e6f-9a7b-1c2d3e4f5a6b";

  private readonly CatalogService _catalog = new(CatalogLoader.Load(Path.Combine(FindRepoRoot(), "catalog")));

  [Fact]
  public void ValidateVariables_AllowsAllOutbound_WhenOutboundRulesNotGiven()
  {
    var result = _catalog.ValidateVariables("firewall", Valid());

    Assert.True(result.IsValid);
    var outbound = result.Values["outbound_rules"]!.AsArray();
    Assert.Equal(["tcp", "udp", "icmp"], outbound.Select(r => r!["protocol"]!.GetValue<string>()));
    Assert.All(outbound, r =>
    {
      Assert.Equal("all", r!["port_range"]!.GetValue<string>());
      Assert.Equal(["0.0.0.0/0", "::/0"], r["destination_addresses"]!.AsArray().Select(a => a!.GetValue<string>()));
    });
  }

  [Theory]
  [InlineData("tcp", "22", "203.0.113.7/32")]
  [InlineData("tcp", "8000-9000", "10.0.0.0/8")]
  [InlineData("udp", "all", "2001:db8::/32")]
  [InlineData("icmp", "all", "::/0")]
  [InlineData("tcp", "22", "0.0.0.0/0")]
  [InlineData("tcp", "65535", "192.168.1.0/24")]
  public void ValidateVariables_AcceptsInboundRule_WhenWellFormed(string protocol, string ports, string source)
  {
    var input = Valid(Rule(protocol, ports, source));

    Assert.True(_catalog.ValidateVariables("firewall", input).IsValid);
  }

  [Theory]
  [InlineData("0")]
  [InlineData("65536")]
  [InlineData("22,80")]
  [InlineData("80-")]
  [InlineData("ALL")]
  [InlineData("")]
  public void ValidateVariables_RejectsPortRange_WhenMalformed(string ports)
  {
    var result = _catalog.ValidateVariables("firewall", Valid(Rule("tcp", ports, "0.0.0.0/0")));

    Assert.True(result.Errors.ContainsKey("inbound_rules[0].port_range"));
  }

  [Theory]
  [InlineData("203.0.113.7")]
  [InlineData("256.0.0.0/8")]
  [InlineData("10.0.0.0/33")]
  [InlineData("my-laptop")]
  [InlineData("rp-3f2b8c1e-5d4a-4e6f-9a7b-1c2d3e4f5a6b")]
  public void ValidateVariables_RejectsSourceAddress_WhenNotCidr(string source)
  {
    var result = _catalog.ValidateVariables("firewall", Valid(Rule("tcp", "22", source)));

    Assert.True(result.Errors.ContainsKey("inbound_rules[0].source_addresses[0]"));
  }

  [Fact]
  public void ValidateVariables_RejectsProtocol_WhenNotTcpUdpOrIcmp()
  {
    var result = _catalog.ValidateVariables("firewall", Valid(Rule("all", "all", "0.0.0.0/0")));

    Assert.True(result.Errors.ContainsKey("inbound_rules[0].protocol"));
  }

  [Theory]
  [InlineData("resource-provisioner")]
  [InlineData("rp-3f2b8c1e-5d4a-4e6f-9a7b-1c2d3e4f5a6b")]
  [InlineData("rp-owner-abc123")]
  [InlineData("3F2B8C1E-5D4A-4E6F-9A7B-1C2D3E4F5A6B")]
  [InlineData("web-1")]
  [InlineData("123456789")]
  public void ValidateVariables_RejectsTarget_WhenNotARequestId(string target)
  {
    var input = Valid();
    input["droplet_request_ids"] = new JsonArray(target);

    var result = _catalog.ValidateVariables("firewall", input);

    Assert.True(result.Errors.ContainsKey("droplet_request_ids[0]"));
  }

  [Fact]
  public void ValidateVariables_RejectsTargets_WhenMoreThanFive()
  {
    var input = Valid();
    input["droplet_request_ids"] = new JsonArray(
      Enumerable.Range(0, 6).Select(i => (JsonNode?)JsonValue.Create(Guid.NewGuid().ToString())).ToArray());

    var result = _catalog.ValidateVariables("firewall", input);

    Assert.True(result.Errors.ContainsKey("droplet_request_ids"));
  }

  [Theory]
  [InlineData("droplet_request_ids")]
  [InlineData("inbound_rules")]
  public void ValidateVariables_RejectsEmptyList_WhenAtLeastOneNeeded(string field)
  {
    var input = Valid();
    input[field] = new JsonArray();

    var result = _catalog.ValidateVariables("firewall", input);

    Assert.True(result.Errors.ContainsKey(field));
  }

  [Theory]
  [InlineData("source_tags")]
  [InlineData("source_droplet_ids")]
  public void ValidateVariables_RejectsRuleField_WhenNotAnAddress(string field)
  {
    var rule = Rule("tcp", "22", "0.0.0.0/0");
    rule[field] = new JsonArray("resource-provisioner");

    var result = _catalog.ValidateVariables("firewall", Valid(rule));

    Assert.Equal(["Unknown field."], result.Errors[$"inbound_rules[0].{field}"]);
  }

  [Theory]
  [InlineData(54, true)]
  [InlineData(55, false)]
  public void ValidateVariables_LeavesRoomForSuffix_WhenCheckingNameLength(int length, bool valid)
  {
    var input = Valid();
    input["name"] = new string('a', length);

    var result = _catalog.ValidateVariables("firewall", input);

    Assert.Equal(valid, result.IsValid);
  }

  [Theory]
  [InlineData("rp_tags")]
  [InlineData("rp_name_suffix")]
  [InlineData("tags")]
  public void ValidateVariables_RejectsRunnerVariable_WhenSetByRequest(string field)
  {
    var input = Valid();
    input[field] = "resource-provisioner";

    var result = _catalog.ValidateVariables("firewall", input);

    Assert.Equal(["Unknown field."], result.Errors[field]);
  }

  private static JsonObject Valid(JsonObject? rule = null) => new()
  {
    ["name"] = "web-fw",
    ["droplet_request_ids"] = new JsonArray(RequestId),
    ["inbound_rules"] = new JsonArray(rule ?? Rule("tcp", "22", "203.0.113.7/32")),
  };

  private static JsonObject Rule(string protocol, string ports, string source) => new()
  {
    ["protocol"] = protocol, ["port_range"] = ports, ["source_addresses"] = new JsonArray(source),
  };

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
