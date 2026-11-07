namespace ResourceProvisioner.Api.Tests;

using System.Net;
using System.Text.Json.Nodes;
using Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Models;
using ResourceCatalog;
using Services.Provisioning;
using Services.Requests;
using Xunit;

/// <summary>
/// DigitalOcean projects: the real <c>project</c> manifest, the types that can go in one, free types and the limits,
/// and the runner passing a project's ID to its members. The fixture <c>test-group</c> stands in for a project and
/// <c>test-firewall</c>'s <c>group_request_id</c> for a member's <c>project_request_id</c>.
/// </summary>
public class ProjectsTests : IClassFixture<HighLimitApiFactory>
{
  private const string RequestId = "3f2b8c1e-5d4a-4e6f-9a7b-1c2d3e4f5a6b";

  private static readonly CatalogService Real =
    new(CatalogLoader.Load(CatalogLoader.ResolveRoot("catalog", AppContext.BaseDirectory)));

  private readonly HighLimitApiFactory _factory;
  private readonly string _userId = $"user-{Guid.NewGuid():N}";

  public ProjectsTests(HighLimitApiFactory factory)
  {
    _factory = factory;
    _factory.Terraform.Reset();
  }

  private HttpClient User => _factory.CreateUserClient(_userId, $"{_userId}@example.com");

  [Fact]
  public void Project_AppliesDefaults_AndIsFree()
  {
    var result = Real.ValidateVariables("project", new JsonObject { ["name"] = "shop" });

    Assert.True(result.IsValid);
    Assert.Equal("Web Application", result.Values["purpose"]!.GetValue<string>());
    Assert.Equal("Development", result.Values["environment"]!.GetValue<string>());
    Assert.True(Real.Find("project")!.Free);
    Assert.DoesNotContain(Real.Find("project")!.Outputs, o => o.Sensitive);
  }

  [Theory]
  [InlineData("environment", "Prod")]
  [InlineData("purpose", "Crypto mining")]
  [InlineData("name", "Shop")]
  public void Project_Rejects_ValuesOutsideItsRules(string variable, string value)
  {
    var input = new JsonObject { ["name"] = "shop", [variable] = value };

    Assert.True(Real.ValidateVariables("project", input).Errors.ContainsKey(variable));
  }

  [Theory]
  [InlineData("project", true)]
  [InlineData("firewall", true)]
  [InlineData("droplet", false)]
  [InlineData("postgres", false)]
  [InlineData("spaces-bucket", false)]
  public void FreeTypes_AreMarked(string type, bool free)
  {
    Assert.Equal(free, Real.Find(type)!.Free);
  }

  [Theory]
  [InlineData("droplet")]
  [InlineData("postgres")]
  [InlineData("spaces-bucket")]
  public void Members_TakeAnOptionalProjectReference(string type)
  {
    var variable = Real.Find(type)!.Variables.Single(v => v.Name == "project_request_id");

    Assert.Equal("project", variable.References);
    Assert.False(variable.Required);
    Assert.Equal("rp_project_id", RunnerVariables.DependencyVariable(variable));
  }

  [Fact]
  public void Firewalls_CantGoInAProject()
  {
    Assert.DoesNotContain(Real.Find("firewall")!.Variables, v => v.References == "project");
  }

  [Theory]
  [InlineData("not-a-guid")]
  [InlineData("3F2B8C1E-5D4A-4E6F-9A7B-1C2D3E4F5A6B")]
  public void Members_RejectAProjectReference_ThatIsntALowercaseGuid(string value)
  {
    var input = new JsonObject { ["name"] = "files", ["project_request_id"] = value };

    Assert.True(Real.ValidateVariables("spaces-bucket", input).Errors.ContainsKey("project_request_id"));
  }

  [Fact]
  public void ForDependencies_SetsTheReferencedRequestsId()
  {
    var manifest = Real.Find("droplet")!;
    var variables = new JsonObject { ["name"] = "web", ["project_request_id"] = RequestId };

    var result = RunnerVariables.ForDependencies(
      manifest,
      variables,
      new Dictionary<Guid, string?> { [Guid.Parse(RequestId)] = """{"id":"do-project-1","name":"shop"}""" });

    Assert.True(JsonNode.DeepEquals(JsonNode.Parse("""{"rp_project_id":"do-project-1"}"""), result));
  }

  [Fact]
  public void ForDependencies_SetsNothing_WithoutAProject()
  {
    var result = RunnerVariables.ForDependencies(
      Real.Find("droplet")!,
      new JsonObject { ["name"] = "web" },
      new Dictionary<Guid, string?>());

    Assert.Empty(result);
  }

  [Fact]
  public void ForDependencies_Throws_WhenTheProjectHasNoIdYet()
  {
    var variables = new JsonObject { ["name"] = "web", ["project_request_id"] = RequestId };

    Assert.Throws<InvalidOperationException>(() => RunnerVariables.ForDependencies(
      Real.Find("droplet")!,
      variables,
      new Dictionary<Guid, string?> { [Guid.Parse(RequestId)] = null }));
  }

  [Fact]
  public async Task Runner_PassesTheGroupsId_ToAMember()
  {
    var group = await _factory.SeedRequestAsync(
      _userId,
      RequestStatus.Succeeded,
      type: "test-group",
      variables: """{"name":"shop"}""",
      outputs: """{"id":"grp-123"}""");
    var firewall = await _factory.SeedRequestAsync(
      _userId,
      RequestStatus.Queued,
      type: "test-firewall",
      variables: $$"""{"rules":[],"group_request_id":"{{group.Id}}"}""");
    await _factory.AddDependencyAsync(firewall.Id, group.Id);

    using (var scope = _factory.Services.CreateScope())
    {
      await scope.ServiceProvider.GetRequiredService<ITerraformJobProcessor>()
        .RunAsync(firewall.Id, CancellationToken.None);
    }

    Assert.Equal(RequestStatus.Succeeded, (await _factory.FindRequestAsync(firewall.Id))!.Status);
    var tfvars = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
      _factory.WorkspacesRoot,
      firewall.Id.ToString(),
      TerraformWorkspace.VariablesFile)))!;
    Assert.Equal("grp-123", tfvars["rp_group_id"]!.GetValue<string>());
    Assert.Equal(group.Id.ToString(), tfvars["group_request_id"]!.GetValue<string>());
  }

  [Fact]
  public async Task FreeTypes_DontCountTowardsTheLimit()
  {
    for (var i = 0; i < 3; i++)
    {
      await _factory.SeedRequestAsync(_userId, RequestStatus.Succeeded);
    }

    var group = await _factory.SeedRequestAsync(
      _userId,
      RequestStatus.AwaitingConfirmation,
      type: "test-group",
      variables: """{"name":"shop"}""");
    var server = await _factory.SeedRequestAsync(_userId, RequestStatus.AwaitingConfirmation);

    var free = await User.PostAsync($"/api/requests/{group.Id}/confirm", null);
    var paid = await User.PostAsync($"/api/requests/{server.Id}/confirm", null);

    Assert.Equal(HttpStatusCode.OK, free.StatusCode);
    Assert.Equal(HttpStatusCode.Conflict, paid.StatusCode);
  }

  [Fact]
  public async Task ActiveFreeTypes_DontUseUpASlot()
  {
    for (var i = 0; i < 3; i++)
    {
      await _factory.SeedRequestAsync(
        _userId,
        RequestStatus.Succeeded,
        type: "test-group",
        variables: """{"name":"shop"}""");
    }

    var server = await _factory.SeedRequestAsync(_userId, RequestStatus.AwaitingConfirmation);

    var response = await User.PostAsync($"/api/requests/{server.Id}/confirm", null);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }
}
