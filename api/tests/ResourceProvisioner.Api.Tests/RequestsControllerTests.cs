namespace ResourceProvisioner.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Data;
using Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Services.Provisioning;
using Xunit;

/// <summary>A test host whose global active-request limit is high, so tests sharing it don't trip it.</summary>
public class HighLimitApiFactory : ApiFactory
{
  /// <inheritdoc />
  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    base.ConfigureWebHost(builder);
    builder.ConfigureAppConfiguration(config =>
      config.AddInMemoryCollection(new Dictionary<string, string?> { ["Limits:MaxActiveRequestsTotal"] = "1000" }));
  }
}

public class RequestsControllerTests(HighLimitApiFactory factory) : IClassFixture<HighLimitApiFactory>
{
  private const string Route = "/api/requests";

  private readonly string _userId = $"user-{Guid.NewGuid():N}";

  private HttpClient User => factory.CreateUserClient(_userId, $"{_userId}@example.com");

  [Theory]
  [InlineData("GET", Route)]
  [InlineData("GET", $"{Route}/00000000-0000-0000-0000-000000000001")]
  [InlineData("POST", $"{Route}/00000000-0000-0000-0000-000000000001/confirm")]
  [InlineData("POST", $"{Route}/00000000-0000-0000-0000-000000000001/cancel")]
  [InlineData("POST", $"{Route}/00000000-0000-0000-0000-000000000001/destroy")]
  [InlineData("GET", $"{Route}/00000000-0000-0000-0000-000000000001/sensitive-outputs")]
  public async Task Endpoints_ReturnUnauthorized_WithoutCredentials(string method, string url)
  {
    var response = await factory.CreateAnonymousClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task List_ReturnsOnlyOwnRequests_NewestFirst_ForUser()
  {
    var older = await factory.SeedRequestAsync(_userId, RequestStatus.Succeeded);
    var newer = await factory.SeedRequestAsync(_userId, RequestStatus.AwaitingConfirmation);
    var others = await factory.SeedRequestAsync("someone-else", RequestStatus.Succeeded);

    var list = await User.GetFromJsonAsync<List<ProvisioningRequestResponse>>(Route);

    Assert.NotNull(list);
    Assert.Equal([newer.Id, older.Id], list.Select(r => r.Id));
    Assert.DoesNotContain(list, r => r.Id == others.Id);
  }

  [Fact]
  public async Task List_ReturnsEveryUsersRequests_WithOwner_ForAdmin()
  {
    var mine = await factory.SeedRequestAsync(_userId, RequestStatus.Succeeded);
    var others = await factory.SeedRequestAsync("someone-else", RequestStatus.Queued);

    var list = await factory.CreateAdminClient().GetFromJsonAsync<List<ProvisioningRequestResponse>>(Route);

    Assert.Contains(list!, r => r.Id == mine.Id && r.OwnerId == _userId && r.OwnerEmail == $"{_userId}@example.com");
    Assert.Contains(list!, r => r.Id == others.Id && r.OwnerId == "someone-else");
  }

  [Fact]
  public async Task Get_ReturnsNotFound_ForAnotherUsersRequest()
  {
    var others = await factory.SeedRequestAsync("someone-else", RequestStatus.Succeeded);

    var response = await User.GetAsync($"{Route}/{others.Id}");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Get_ReturnsAnotherUsersRequest_ForAdmin()
  {
    var others = await factory.SeedRequestAsync("someone-else", RequestStatus.Succeeded);

    var request = await factory.CreateAdminClient().GetFromJsonAsync<ProvisioningRequestResponse>($"{Route}/{others.Id}");

    Assert.Equal("someone-else", request!.OwnerId);
  }

  [Fact]
  public async Task Get_ReturnsOutputs_WithoutSensitiveOutputsOrVariables()
  {
    var request = await factory.SeedRequestAsync(
      _userId,
      RequestStatus.Succeeded,
      variables: """{"name":"web-1","size":"small","admin_password":"var-secret"}""",
      outputs: """{"id":"123","ipv4_address":"192.0.2.1"}""",
      sensitiveOutputs: """{"root_password":"output-secret"}""");

    var json = await User.GetStringAsync($"{Route}/{request.Id}");

    Assert.DoesNotContain("secret", json);
    Assert.DoesNotContain("admin_password", json);
    Assert.DoesNotContain("root_password", json);
    var body = JsonNode.Parse(json)!;
    Assert.Equal("192.0.2.1", body["outputs"]!["ipv4_address"]!.GetValue<string>());
    Assert.Equal("Succeeded", body["status"]!.GetValue<string>());
  }

  [Fact]
  public async Task Get_RendersIdAsLowercaseGuid()
  {
    var request = await factory.SeedRequestAsync(_userId, RequestStatus.Succeeded);

    var body = JsonNode.Parse(await User.GetStringAsync($"{Route}/{request.Id.ToString().ToUpperInvariant()}"))!;

    Assert.Equal(request.Id.ToString("D"), body["id"]!.GetValue<string>());
    Assert.Matches("^[0-9a-f-]{36}$", body["id"]!.GetValue<string>());
  }

  [Fact]
  public async Task Confirm_QueuesRequest_ForOwner()
  {
    var request = await factory.SeedRequestAsync(_userId, RequestStatus.AwaitingConfirmation);

    var response = await User.PostAsync($"{Route}/{request.Id}/confirm", null);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<ProvisioningRequestResponse>();
    Assert.Equal(RequestStatus.Queued, body!.Status);
    Assert.NotNull(body.ConfirmedAt);
    Assert.Equal(RequestStatus.Queued, (await factory.FindRequestAsync(request.Id))!.Status);
  }

  [Fact]
  public async Task Confirm_PutsRequestOnRunnerQueue()
  {
    var request = await factory.SeedRequestAsync(_userId, RequestStatus.AwaitingConfirmation);

    await User.PostAsync($"{Route}/{request.Id}/confirm", null);

    await AssertQueued(request.Id);
  }

  [Theory]
  [InlineData(RequestStatus.Queued)]
  [InlineData(RequestStatus.Applying)]
  [InlineData(RequestStatus.Succeeded)]
  [InlineData(RequestStatus.Failed)]
  [InlineData(RequestStatus.Destroying)]
  [InlineData(RequestStatus.Destroyed)]
  [InlineData(RequestStatus.Cancelled)]
  public async Task Confirm_ReturnsConflict_WhenNotAwaitingConfirmation(RequestStatus status)
  {
    var request = await factory.SeedRequestAsync(_userId, status);

    var response = await User.PostAsync($"{Route}/{request.Id}/confirm", null);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    Assert.Equal($"This request is {status}, so it can't be confirmed.", problem!.Detail);
    Assert.Equal(status, (await factory.FindRequestAsync(request.Id))!.Status);
  }

  [Fact]
  public async Task Confirm_ReturnsNotFound_ForAnotherUsersRequest()
  {
    var others = await factory.SeedRequestAsync("someone-else", RequestStatus.AwaitingConfirmation);

    var response = await User.PostAsync($"{Route}/{others.Id}/confirm", null);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.Equal(RequestStatus.AwaitingConfirmation, (await factory.FindRequestAsync(others.Id))!.Status);
  }

  [Theory]
  [InlineData(RequestStatus.Succeeded)]
  [InlineData(RequestStatus.Failed)]
  public async Task Destroy_QueuesDestroy_ForOwner(RequestStatus status)
  {
    var request = await factory.SeedRequestAsync(_userId, status);

    var response = await User.PostAsync($"{Route}/{request.Id}/destroy", null);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<ProvisioningRequestResponse>();
    Assert.Equal(RequestStatus.Destroying, body!.Status);
    Assert.Equal(RequestStatus.Destroying, (await factory.FindRequestAsync(request.Id))!.Status);
    await AssertQueued(request.Id);
  }

  [Theory]
  [InlineData(RequestStatus.AwaitingConfirmation)]
  [InlineData(RequestStatus.Queued)]
  [InlineData(RequestStatus.Applying)]
  [InlineData(RequestStatus.Destroying)]
  [InlineData(RequestStatus.Destroyed)]
  [InlineData(RequestStatus.Cancelled)]
  public async Task Destroy_ReturnsConflict_WhenNotSucceededOrFailed(RequestStatus status)
  {
    var request = await factory.SeedRequestAsync(_userId, status);

    var response = await User.PostAsync($"{Route}/{request.Id}/destroy", null);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    Assert.Equal($"This request is {status}, so it can't be destroyed.", problem!.Detail);
    Assert.Equal(status, (await factory.FindRequestAsync(request.Id))!.Status);
  }

  [Fact]
  public async Task Destroy_ReturnsNotFound_ForAnotherUsersRequest()
  {
    var others = await factory.SeedRequestAsync("someone-else", RequestStatus.Succeeded);

    var response = await User.PostAsync($"{Route}/{others.Id}/destroy", null);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.Equal(RequestStatus.Succeeded, (await factory.FindRequestAsync(others.Id))!.Status);
  }

  [Fact]
  public async Task Destroy_QueuesDestroy_ForAdminOnAnotherUsersRequest()
  {
    var others = await factory.SeedRequestAsync("someone-else", RequestStatus.Succeeded);

    var response = await factory.CreateAdminClient().PostAsync($"{Route}/{others.Id}/destroy", null);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(RequestStatus.Destroying, (await factory.FindRequestAsync(others.Id))!.Status);
  }

  [Fact]
  public async Task Destroy_FreesAnActiveSlot_OnceDestroyed()
  {
    var active = new List<ProvisioningRequest>();
    for (var i = 0; i < 3; i++)
    {
      active.Add(await factory.SeedRequestAsync(_userId, RequestStatus.Succeeded));
    }

    var next = await factory.SeedRequestAsync(_userId, RequestStatus.AwaitingConfirmation);
    await User.PostAsync($"{Route}/{active[0].Id}/destroy", null);

    // Destroying still counts: the resource exists until Terraform has removed it.
    Assert.Equal(HttpStatusCode.Conflict, (await User.PostAsync($"{Route}/{next.Id}/confirm", null)).StatusCode);

    using (var scope = factory.Services.CreateScope())
    {
      await scope.ServiceProvider.GetRequiredService<ITerraformJobProcessor>()
        .RunAsync(active[0].Id, CancellationToken.None);
    }

    Assert.Equal(RequestStatus.Destroyed, (await factory.FindRequestAsync(active[0].Id))!.Status);
    Assert.Equal(HttpStatusCode.OK, (await User.PostAsync($"{Route}/{next.Id}/confirm", null)).StatusCode);
  }

  [Fact]
  public async Task SensitiveOutputs_ReturnsTheSecrets_ForOwner_WithoutCaching()
  {
    var request = await factory.SeedRequestAsync(
      _userId,
      RequestStatus.Succeeded,
      outputs: """{"id":"123"}""",
      sensitiveOutputs: """{"root_password":"output-secret"}""");

    var response = await User.GetAsync($"{Route}/{request.Id}/sensitive-outputs");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.True(JsonNode.DeepEquals(
      JsonNode.Parse("""{"root_password":"output-secret"}"""),
      JsonNode.Parse(await response.Content.ReadAsStringAsync())));
    Assert.True(response.Headers.CacheControl?.NoStore);
  }

  [Fact]
  public async Task SensitiveOutputs_ReturnsEmptyObject_WhenThereAreNone()
  {
    var request = await factory.SeedRequestAsync(_userId, RequestStatus.Succeeded);

    var json = await User.GetStringAsync($"{Route}/{request.Id}/sensitive-outputs");

    Assert.Equal("{}", json);
  }

  [Fact]
  public async Task SensitiveOutputs_ReturnsNotFound_ForAnotherUsersRequest()
  {
    var others = await factory.SeedRequestAsync(
      "someone-else",
      RequestStatus.Succeeded,
      sensitiveOutputs: """{"root_password":"output-secret"}""");

    var response = await User.GetAsync($"{Route}/{others.Id}/sensitive-outputs");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.DoesNotContain("output-secret", await response.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task SensitiveOutputs_ReturnsForbidden_ForAdminOnAnotherUsersRequest()
  {
    var others = await factory.SeedRequestAsync(
      "someone-else",
      RequestStatus.Succeeded,
      sensitiveOutputs: """{"root_password":"output-secret"}""");

    var response = await factory.CreateAdminClient().GetAsync($"{Route}/{others.Id}/sensitive-outputs");

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    Assert.DoesNotContain("output-secret", await response.Content.ReadAsStringAsync());
  }

  [Theory]
  [InlineData("confirm")]
  [InlineData("cancel")]
  public async Task Action_ReturnsForbidden_ForAdminOnAnotherUsersRequest(string action)
  {
    var others = await factory.SeedRequestAsync("someone-else", RequestStatus.AwaitingConfirmation);

    var response = await factory.CreateAdminClient().PostAsync($"{Route}/{others.Id}/{action}", null);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    Assert.Equal($"Only the request's owner can {action} it.", problem!.Detail);
    Assert.Equal(RequestStatus.AwaitingConfirmation, (await factory.FindRequestAsync(others.Id))!.Status);
  }

  [Fact]
  public async Task Confirm_QueuesRequest_ForAdminOnOwnRequest()
  {
    var request = await factory.SeedRequestAsync("admin-1", RequestStatus.AwaitingConfirmation);

    var response = await factory.CreateAdminClient().PostAsync($"{Route}/{request.Id}/confirm", null);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Confirm_ReturnsUnprocessable_WhenReferencedDropletIsNoLongerSucceeded()
  {
    var droplet = await factory.SeedServerAsync(_userId);
    var request = await factory.SeedRequestAsync(
      _userId,
      RequestStatus.AwaitingConfirmation,
      "test-database",
      $$"""{"name":"db","allowed_droplet_request_ids":["{{droplet.Id}}"],"allowed_ip_addresses":[]}""");
    await SetStatus(droplet.Id, RequestStatus.Destroying);

    var response = await User.PostAsync($"{Route}/{request.Id}/confirm", null);

    Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
    Assert.Contains("allowed_droplet_request_ids[0]", problem!.Errors.Keys);
    Assert.Equal(RequestStatus.AwaitingConfirmation, (await factory.FindRequestAsync(request.Id))!.Status);
  }

  [Fact]
  public async Task Confirm_ReturnsUnprocessable_WhenReferencedDropletIsAnotherUsers()
  {
    var droplet = await factory.SeedServerAsync("someone-else");
    var request = await factory.SeedRequestAsync(
      _userId,
      RequestStatus.AwaitingConfirmation,
      "test-database",
      $$"""{"name":"db","allowed_droplet_request_ids":["{{droplet.Id}}"]}""");

    var response = await User.PostAsync($"{Route}/{request.Id}/confirm", null);

    Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
  }

  [Fact]
  public async Task Confirm_QueuesRequest_WhenReferencedDropletIsOwnAndSucceeded()
  {
    var droplet = await factory.SeedServerAsync(_userId);
    var request = await factory.SeedRequestAsync(
      _userId,
      RequestStatus.AwaitingConfirmation,
      "test-database",
      $$"""{"name":"db","allowed_droplet_request_ids":["{{droplet.Id}}"]}""");

    var response = await User.PostAsync($"{Route}/{request.Id}/confirm", null);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Cancel_CancelsRequest_ForOwner()
  {
    var request = await factory.SeedRequestAsync(_userId, RequestStatus.AwaitingConfirmation);

    var response = await User.PostAsync($"{Route}/{request.Id}/cancel", null);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(RequestStatus.Cancelled, (await factory.FindRequestAsync(request.Id))!.Status);

    var confirm = await User.PostAsync($"{Route}/{request.Id}/confirm", null);
    Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
  }

  [Theory]
  [InlineData(RequestStatus.Queued)]
  [InlineData(RequestStatus.Applying)]
  [InlineData(RequestStatus.Succeeded)]
  [InlineData(RequestStatus.Cancelled)]
  public async Task Cancel_ReturnsConflict_WhenNotAwaitingConfirmation(RequestStatus status)
  {
    var request = await factory.SeedRequestAsync(_userId, status);

    var response = await User.PostAsync($"{Route}/{request.Id}/cancel", null);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    Assert.Equal(status, (await factory.FindRequestAsync(request.Id))!.Status);
  }

  [Fact]
  public async Task Cancel_ReturnsNotFound_ForAnotherUsersRequest()
  {
    var others = await factory.SeedRequestAsync("someone-else", RequestStatus.AwaitingConfirmation);

    var response = await User.PostAsync($"{Route}/{others.Id}/cancel", null);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  /// <summary>
  /// Reads the runner's queue until <paramref name="requestId"/> turns up; fails after 5 seconds. The runner's
  /// background service is off in tests, so the queue holds every request this class confirmed or destroyed.
  /// </summary>
  private async Task AssertQueued(Guid requestId)
  {
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await foreach (var id in factory.Services.GetRequiredService<IJobDispatcher>().ReadAllAsync(cts.Token))
    {
      if (id == requestId)
      {
        return;
      }
    }
  }

  private async Task SetStatus(Guid id, RequestStatus status)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var request = await db.ProvisioningRequests.FindAsync(id);
    request!.Status = status;
    await db.SaveChangesAsync();
  }
}
