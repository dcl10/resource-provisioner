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
/// DigitalOcean projects in the catalog and the runner: the real <c>project</c> manifest, which modules take the
/// project's ID, free types and the limits, and the runner passing <c>rp_project_id</c> (and a reference's
/// <c>rp_&lt;x&gt;_id</c>, with the fixture <c>test-group</c>) to a module.
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
    Assert.Contains(Real.Find("project")!.Outputs, o => o.Name == RunnerVariables.DependencyOutput);
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
  [InlineData("droplet", true)]
  [InlineData("postgres", true)]
  [InlineData("spaces-bucket", true)]
  [InlineData("firewall", false)]
  [InlineData("project", false)]
  public void OnlyTypesThatCanBeInADigitalOceanProject_TakeItsId(string type, bool takesIt)
  {
    Assert.Equal(takesIt, Real.ModuleDeclares(type, RunnerVariables.ProjectIdVariable));
  }

  [Fact]
  public void NoManifest_TakesAProjectReference()
  {
    Assert.All(Real.Types, t => Assert.DoesNotContain(t.Variables, v => v.References == "project"));
    Assert.All(Real.Types, t => Assert.DoesNotContain(t.Variables, v => v.Name == "project_request_id"));
  }

  [Fact]
  public void ForDependencies_SetsTheProjectsId()
  {
    var result = RunnerVariables.ForDependencies(
      Real.Find("droplet")!,
      new JsonObject { ["name"] = "web" },
      new Dictionary<Guid, string?> { [Guid.Parse(RequestId)] = """{"id":"do-project-1","name":"shop"}""" },
      Guid.Parse(RequestId));

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
    Assert.Throws<InvalidOperationException>(() => RunnerVariables.ForDependencies(
      Real.Find("droplet")!,
      new JsonObject { ["name"] = "web" },
      new Dictionary<Guid, string?> { [Guid.Parse(RequestId)] = null },
      Guid.Parse(RequestId)));
  }

  [Fact]
  public async Task Runner_PassesTheProjectsId_ToAModuleThatTakesIt()
  {
    var project = await _factory.SeedProjectAsync(_userId, name: "shop");
    var server = await _factory.SeedRequestAsync(_userId, RequestStatus.Queued, projectId: project.Id);
    await _factory.AddDependencyAsync(server.Id, project.ProjectRequestId);

    await Run(server.Id);

    Assert.Equal(RequestStatus.Succeeded, (await _factory.FindRequestAsync(server.Id))!.Status);
    var tfvars = await Tfvars(server.Id);
    Assert.Equal("prj-shop", tfvars["rp_project_id"]!.GetValue<string>());
    var saved = JsonNode.Parse((await _factory.FindRequestAsync(server.Id))!.TerraformVariables!)!;
    Assert.Equal("prj-shop", saved["rp_project_id"]!.GetValue<string>());
  }

  [Fact]
  public async Task Runner_LeavesTheProjectsIdOut_OfAModuleThatDoesntTakeIt()
  {
    var project = await _factory.SeedProjectAsync(_userId);
    var firewall = await _factory.SeedRequestAsync(
      _userId,
      RequestStatus.Queued,
      type: "test-firewall",
      variables: """{"rules":[]}""",
      projectId: project.Id);

    await Run(firewall.Id);

    Assert.Equal(RequestStatus.Succeeded, (await _factory.FindRequestAsync(firewall.Id))!.Status);
    Assert.Null((await Tfvars(firewall.Id))["rp_project_id"]);
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

    await Run(firewall.Id);

    Assert.Equal(RequestStatus.Succeeded, (await _factory.FindRequestAsync(firewall.Id))!.Status);
    var tfvars = await Tfvars(firewall.Id);
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

  private async Task Run(Guid requestId)
  {
    using var scope = _factory.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ITerraformJobProcessor>().RunAsync(requestId, CancellationToken.None);
  }

  private async Task<JsonNode> Tfvars(Guid requestId) =>
    JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(
      _factory.WorkspacesRoot,
      requestId.ToString(),
      TerraformWorkspace.VariablesFile)))!;
}
