namespace ResourceProvisioner.Api.Tests;

using System.Text.Json.Nodes;
using Data;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Services.Requests;
using Xunit;

/// <summary>
/// <c>ProposeAsync</c>, which the agent's <c>propose_request</c> tool and <c>POST /api/projects</c> call, and the
/// project checks at confirm.
/// </summary>
public class ProvisioningRequestServiceTests(HighLimitApiFactory factory) : IClassFixture<HighLimitApiFactory>
{
  private const string DatabaseType = "test-database";

  private readonly RequestUser _owner = new($"owner-{Guid.NewGuid():N}", "owner@example.com", IsAdmin: false);

  [Fact]
  public async Task ProposeAsync_CreatesRequestAwaitingConfirmation_WithDefaultsApplied()
  {
    var conversationId = Guid.NewGuid();

    var result = await Propose(_owner, "test-server", """{ "name": "web-1" }""", conversationId);

    Assert.True(result.IsValid);
    Assert.Empty(result.Errors);
    var request = result.Request!;
    Assert.Equal(RequestStatus.AwaitingConfirmation, request.Status);
    Assert.Equal(_owner.UserId, request.OwnerId);
    Assert.Equal("owner@example.com", request.OwnerEmail);
    Assert.Equal("Test server", request.DisplayName);
    Assert.Equal(conversationId, request.ConversationId);
    Assert.Null(request.ConfirmedAt);

    var stored = await factory.FindRequestAsync(request.Id);
    Assert.NotNull(stored);
    Assert.True(JsonNode.DeepEquals(
      JsonNode.Parse("""{ "name": "web-1", "size": "small", "node_count": 1, "backups": false, "tags": [] }"""),
      JsonNode.Parse(stored.Variables)));
  }

  [Fact]
  public async Task ProposeAsync_ReturnsErrorsAndSavesNothing_WhenVariablesAreInvalid()
  {
    var result = await Propose(_owner, "test-server", """{ "size": "huge" }""");

    Assert.False(result.IsValid);
    Assert.Equal(["Required."], result.Errors["name"]);
    Assert.Contains("size", result.Errors.Keys);
    Assert.Equal(0, await CountOwnedBy(_owner.UserId));
  }

  [Fact]
  public async Task ProposeAsync_ReturnsTypeError_WhenTypeIsUnknown()
  {
    var result = await Propose(_owner, "nope", "{}");

    Assert.Equal(["Unknown resource type 'nope'."], result.Errors["type"]);
  }

  [Fact]
  public async Task ProposeAsync_RejectsBothVariables_WhenNeitherOfAtLeastOneOfIsSet()
  {
    var result = await Propose(_owner, DatabaseType, """{ "name": "db" }""");

    Assert.False(result.IsValid);
    Assert.Contains("allowed_droplet_request_ids", result.Errors.Keys);
    Assert.Contains("allowed_ip_addresses", result.Errors.Keys);
    Assert.Equal(0, await CountOwnedBy(_owner.UserId));
  }

  [Fact]
  public async Task ProposeAsync_Accepts_WhenDropletReferenceIsOwnSucceededDroplet()
  {
    var droplet = await factory.SeedServerAsync(_owner.UserId);

    var result = await Propose(_owner, DatabaseType, DatabaseReferencing(droplet.Id));

    Assert.True(result.IsValid);
  }

  [Fact]
  public async Task ProposeAsync_RejectsReference_WhenDropletIsAnotherUsers()
  {
    var droplet = await factory.SeedServerAsync("someone-else");

    var result = await Propose(_owner, DatabaseType, DatabaseReferencing(droplet.Id));

    Assert.Equal([$"'{droplet.Id}' isn't the request ID of one of your Test server requests."],
      result.Errors["allowed_droplet_request_ids[0]"]);
  }

  [Fact]
  public async Task ProposeAsync_RejectsReference_WhenAdminNamesAnotherUsersDroplet()
  {
    var admin = new RequestUser($"admin-{Guid.NewGuid():N}", "admin@example.com", IsAdmin: true);
    var droplet = await factory.SeedServerAsync("someone-else");

    var result = await Propose(admin, DatabaseType, DatabaseReferencing(droplet.Id));

    Assert.Contains("allowed_droplet_request_ids[0]", result.Errors.Keys);
  }

  [Theory]
  [InlineData(RequestStatus.AwaitingConfirmation)]
  [InlineData(RequestStatus.Queued)]
  [InlineData(RequestStatus.Applying)]
  [InlineData(RequestStatus.Succeeded)]
  public async Task ProposeAsync_AcceptsAndRecordsReference_WhenOwnRequestIsPendingOrProvisioned(RequestStatus status)
  {
    var server = await factory.SeedServerAsync(_owner.UserId, status);

    var result = await Propose(_owner, DatabaseType, DatabaseReferencing(server.Id));

    Assert.True(result.IsValid);
    var link = Assert.Single(References(result));
    Assert.Equal(
      (server.Id, "test-server", "Test server", "web-1", status),
      (link.Id, link.Type, link.DisplayName, link.Name, link.Status));
    using var scope = factory.Services.CreateScope();
    Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().RequestDependencies
      .AnyAsync(d => d.RequestId == result.Request!.Id && d.DependsOnId == server.Id));
  }

  [Theory]
  [InlineData(RequestStatus.Failed)]
  [InlineData(RequestStatus.Destroying)]
  public async Task ProposeAsync_RejectsReference_WhenOwnRequestCantBeBuiltOn(RequestStatus status)
  {
    var server = await factory.SeedServerAsync(_owner.UserId, status);

    var result = await Propose(_owner, DatabaseType, DatabaseReferencing(server.Id));

    Assert.Equal(
      [$"Test server request {server.Id} is {status}; only pending or provisioned requests can be referenced."],
      result.Errors["allowed_droplet_request_ids[0]"]);
  }

  [Fact]
  public async Task ProposeAsync_RejectsReference_WhenRequestIsOfAnotherType()
  {
    var firewall = await factory.SeedRequestAsync(_owner.UserId, RequestStatus.Succeeded, type: "test-firewall");

    var result = await Propose(_owner, DatabaseType, DatabaseReferencing(firewall.Id));

    Assert.Contains("allowed_droplet_request_ids[0]", result.Errors.Keys);
  }

  [Fact]
  public async Task ProposeAsync_RecordsOneDependency_WhenTheSameRequestIsReferencedTwice()
  {
    var server = await factory.SeedServerAsync(_owner.UserId);

    var result = await Propose(_owner, DatabaseType, DatabaseReferencing(server.Id, server.Id));

    Assert.Single(References(result));
  }

  [Fact]
  public async Task ProposeAsync_ReportsEachBadReference_ByIndex()
  {
    var mine = await factory.SeedServerAsync(_owner.UserId);

    var result = await Propose(_owner, DatabaseType, DatabaseReferencing(mine.Id, Guid.NewGuid()));

    Assert.Equal(["allowed_droplet_request_ids[1]"], result.Errors.Keys);
  }

  [Fact]
  public async Task ProposeAsync_UsesLowercaseGuidIds()
  {
    var result = await Propose(_owner, "test-server", """{ "name": "web-1" }""");

    var id = result.Request!.Id.ToString();
    Assert.Equal(id.ToLowerInvariant(), id);
    Assert.Matches("^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", id);
  }

  [Fact]
  public async Task ProposeAsync_CreatesItsProject_ForAProjectRequest()
  {
    var result = await Propose(_owner, Project.RequestType, """{ "name": "shop", "purpose": "Service or API" }""");

    Assert.True(result.IsValid);
    var request = result.Request!;
    Assert.Empty(request.DependsOn);
    using var scope = factory.Services.CreateScope();
    var project = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Projects
      .SingleAsync(p => p.ProjectRequestId == request.Id);
    Assert.Equal(
      (_owner.UserId, _owner.Email, "shop", null, "Service or API", "Development"),
      (project.OwnerId, project.OwnerEmail, project.Name, project.Description, project.Purpose, project.Environment));
    Assert.Equal(new ProjectLink(project.Id, "shop"), request.Project);
    Assert.Null((await factory.FindRequestAsync(request.Id))!.ProjectId);
  }

  [Fact]
  public async Task ProposeAsync_RejectsAProjectId_OnAProjectRequest()
  {
    var other = await factory.SeedProjectAsync(_owner.UserId);

    var result = await ProposeIn(other.Id, Project.RequestType, """{ "name": "shop" }""");

    Assert.Equal(["A project can't belong to another project; leave it out."],
      result.Errors[ProvisioningRequestService.ProjectErrorKey]);
  }

  [Fact]
  public async Task ProposeAsync_RequiresAProject_ForEveryOtherType()
  {
    var result = await ProposeIn(null, "test-server", """{ "name": "web-1" }""");

    Assert.False(result.IsValid);
    Assert.Equal(["Required: the ID of one of your projects to put this in."],
      result.Errors[ProvisioningRequestService.ProjectErrorKey]);
    Assert.Equal(0, await CountOwnedBy(_owner.UserId));
  }

  [Fact]
  public async Task ProposeAsync_PutsTheRequestInTheProject_AndDependsOnItsRequest()
  {
    var project = await factory.SeedProjectAsync(_owner.UserId, name: "shop");

    var result = await ProposeIn(project.Id, "test-server", """{ "name": "web-1" }""");

    Assert.True(result.IsValid);
    Assert.Equal(new ProjectLink(project.Id, "shop"), result.Request!.Project);
    var link = Assert.Single(result.Request.DependsOn);
    Assert.Equal((project.ProjectRequestId, Project.RequestType), (link.Id, link.Type));
    Assert.Equal(project.Id, (await factory.FindRequestAsync(result.Request.Id))!.ProjectId);
    using var scope = factory.Services.CreateScope();
    Assert.True(await scope.ServiceProvider.GetRequiredService<AppDbContext>().RequestDependencies
      .AnyAsync(d => d.RequestId == result.Request.Id && d.DependsOnId == project.ProjectRequestId));
  }

  [Theory]
  [InlineData(RequestStatus.AwaitingConfirmation)]
  [InlineData(RequestStatus.Queued)]
  [InlineData(RequestStatus.Applying)]
  [InlineData(RequestStatus.Succeeded)]
  public async Task ProposeAsync_Accepts_WhenTheProjectIsPendingOrProvisioned(RequestStatus status)
  {
    var project = await factory.SeedProjectAsync(_owner.UserId, status);

    var result = await ProposeIn(project.Id, "test-server", """{ "name": "web-1" }""");

    Assert.True(result.IsValid);
  }

  [Theory]
  [InlineData(RequestStatus.Failed)]
  [InlineData(RequestStatus.Destroying)]
  public async Task ProposeAsync_Rejects_WhenTheProjectCantBeBuiltOn(RequestStatus status)
  {
    var project = await factory.SeedProjectAsync(_owner.UserId, status, name: "shop");

    var result = await ProposeIn(project.Id, "test-server", """{ "name": "web-1" }""");

    Assert.Equal([$"Project \"shop\" is {status}; only pending or provisioned projects can be used."],
      result.Errors[ProvisioningRequestService.ProjectErrorKey]);
  }

  [Fact]
  public async Task ProposeAsync_RejectsAnotherUsersProject_AsIfItDidntExist()
  {
    var others = await factory.SeedProjectAsync("someone-else");
    var admin = new RequestUser($"admin-{Guid.NewGuid():N}", "admin@example.com", IsAdmin: true);

    var mine = await ProposeIn(others.Id, "test-server", """{ "name": "web-1" }""");
    var asAdmin = await ProposeIn(others.Id, "test-server", """{ "name": "web-1" }""", admin);
    var missing = await ProposeIn(Guid.NewGuid(), "test-server", """{ "name": "web-1" }""");

    Assert.Equal([$"'{others.Id}' isn't the ID of one of your projects."],
      mine.Errors[ProvisioningRequestService.ProjectErrorKey]);
    Assert.Contains(ProvisioningRequestService.ProjectErrorKey, asAdmin.Errors.Keys);
    Assert.Contains(ProvisioningRequestService.ProjectErrorKey, missing.Errors.Keys);
  }

  [Fact]
  public async Task ConfirmManyAsync_QueuesAPendingProjectBeforeItsMember_AndRefusesTheMemberAlone()
  {
    var proposed = await Propose(_owner, Project.RequestType, """{ "name": "shop" }""");
    var member = await ProposeIn(proposed.Request!.Project!.Id, "test-server", """{ "name": "web-1" }""");
    using var scope = factory.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IProvisioningRequestService>();

    await Assert.ThrowsAsync<RequestConflictException>(() => service.ConfirmAsync(member.Request!.Id, _owner));
    var confirmed = await service.ConfirmManyAsync([member.Request!.Id, proposed.Request.Id], _owner);

    Assert.All(confirmed, r => Assert.Equal(RequestStatus.Queued, r.Status));
    var project = await factory.FindRequestAsync(proposed.Request.Id);
    var server = await factory.FindRequestAsync(member.Request.Id);
    Assert.True(project!.ConfirmedAt <= server!.ConfirmedAt);
  }

  [Fact]
  public async Task ConfirmAsync_Refuses_WhenTheProjectWasCancelledMeanwhile()
  {
    var proposed = await Propose(_owner, Project.RequestType, """{ "name": "shop" }""");
    var member = await ProposeIn(proposed.Request!.Project!.Id, "test-server", """{ "name": "web-1" }""");
    using var scope = factory.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IProvisioningRequestService>();
    await service.CancelAsync(proposed.Request.Id, _owner);

    var ex = await Assert.ThrowsAsync<RequestValidationException>(
      () => service.ConfirmAsync(member.Request!.Id, _owner));

    Assert.Null((await factory.FindRequestAsync(member.Request!.Id))!.ProjectId);
    Assert.Equal(
      ["It isn't in a project any more (its project may have been cancelled or destroyed); ask for it again in " +
       "one of your projects."],
      ex.Errors[ProvisioningRequestService.ProjectErrorKey]);
  }

  [Fact]
  public async Task ConfirmAsync_Refuses_WhenAReferencedRequestWasCancelledMeanwhile()
  {
    var server = await Propose(_owner, "test-server", """{ "name": "web-1" }""");
    var database = await Propose(_owner, DatabaseType, DatabaseReferencing(server.Request!.Id));
    using var scope = factory.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IProvisioningRequestService>();
    await service.CancelAsync(server.Request.Id, _owner);

    var ex = await Assert.ThrowsAsync<RequestValidationException>(
      () => service.ConfirmAsync(database.Request!.Id, _owner));

    Assert.Equal(
      [$"'{server.Request.Id}' isn't the request ID of one of your Test server requests."],
      ex.Errors["allowed_droplet_request_ids[0]"]);
  }

  [Fact]
  public async Task ProposeAsync_RejectsReference_ToACancelledRequest()
  {
    var server = await Propose(_owner, "test-server", """{ "name": "web-1" }""");
    using (var scope = factory.Services.CreateScope())
    {
      await scope.ServiceProvider.GetRequiredService<IProvisioningRequestService>()
        .CancelAsync(server.Request!.Id, _owner);
    }

    var result = await Propose(_owner, DatabaseType, DatabaseReferencing(server.Request!.Id));

    Assert.Equal(
      [$"'{server.Request.Id}' isn't the request ID of one of your Test server requests."],
      result.Errors["allowed_droplet_request_ids[0]"]);
  }

  [Fact]
  public async Task ConfirmAsync_Refuses_ARequestInNoProject()
  {
    var server = await factory.SeedRequestAsync(
      _owner.UserId,
      RequestStatus.AwaitingConfirmation,
      withoutProject: true);
    using var scope = factory.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IProvisioningRequestService>();

    var ex = await Assert.ThrowsAsync<RequestValidationException>(() => service.ConfirmAsync(server.Id, _owner));

    Assert.Contains(ProvisioningRequestService.ProjectErrorKey, ex.Errors.Keys);
  }

  private async Task<ProposeResult> ProposeIn(Guid? projectId, string type, string json, RequestUser? owner = null)
  {
    using var scope = factory.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IProvisioningRequestService>();
    return await service.ProposeAsync(owner ?? _owner, type, JsonNode.Parse(json)!.AsObject(), projectId, null);
  }

  private static string DatabaseReferencing(params Guid[] ids) =>
    new JsonObject
    {
      ["name"] = "db",
      ["allowed_droplet_request_ids"] = new JsonArray([.. ids.Select(id => JsonValue.Create(id.ToString()))]),
    }.ToJsonString();

  /// <summary>Proposes a request in the owner's default project (a project request in none).</summary>
  private async Task<ProposeResult> Propose(RequestUser owner, string type, string json, Guid? conversationId = null)
  {
    Guid? projectId = type == Project.RequestType ? null : (await factory.DefaultProjectAsync(owner.UserId)).Id;
    using var scope = factory.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IProvisioningRequestService>();
    return await service.ProposeAsync(owner, type, JsonNode.Parse(json)!.AsObject(), projectId, conversationId);
  }

  private static IEnumerable<RequestLink> References(ProposeResult result) =>
    result.Request!.DependsOn.Where(l => l.Type != Project.RequestType);

  private async Task<int> CountOwnedBy(string ownerId)
  {
    using var scope = factory.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
      .ProvisioningRequests.CountAsync(x => x.OwnerId == ownerId && x.Type != Project.RequestType);
  }
}
