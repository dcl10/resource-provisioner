namespace ResourceProvisioner.Api.Tests;

using System.Text.Json.Nodes;
using Data;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Services.Requests;
using Xunit;

/// <summary><c>ProposeAsync</c>, which the agent's <c>propose_request</c> tool (#17) will call.</summary>
public class ProvisioningRequestServiceTests(ApiFactory factory) : IClassFixture<ApiFactory>
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
    var link = Assert.Single(result.Request!.DependsOn);
    Assert.Equal(
      (server.Id, "test-server", "Test server", "web-1", status),
      (link.Id, link.Type, link.DisplayName, link.Name, link.Status));
    using var scope = factory.Services.CreateScope();
    var dependency = await scope.ServiceProvider.GetRequiredService<AppDbContext>().RequestDependencies
      .SingleAsync(d => d.RequestId == result.Request.Id);
    Assert.Equal(server.Id, dependency.DependsOnId);
  }

  [Theory]
  [InlineData(RequestStatus.Failed)]
  [InlineData(RequestStatus.Destroying)]
  [InlineData(RequestStatus.Destroyed)]
  [InlineData(RequestStatus.Cancelled)]
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

    Assert.Single(result.Request!.DependsOn);
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

  private static string DatabaseReferencing(params Guid[] ids) =>
    new JsonObject
    {
      ["name"] = "db",
      ["allowed_droplet_request_ids"] = new JsonArray([.. ids.Select(id => JsonValue.Create(id.ToString()))]),
    }.ToJsonString();

  private async Task<ProposeResult> Propose(RequestUser owner, string type, string json, Guid? conversationId = null)
  {
    using var scope = factory.Services.CreateScope();
    var service = scope.ServiceProvider.GetRequiredService<IProvisioningRequestService>();
    return await service.ProposeAsync(owner, type, JsonNode.Parse(json)!.AsObject(), conversationId);
  }

  private async Task<int> CountOwnedBy(string ownerId)
  {
    using var scope = factory.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
      .ProvisioningRequests.CountAsync(x => x.OwnerId == ownerId);
  }
}
