namespace ResourceProvisioner.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using Data;
using Fixtures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Services.Provisioning;
using Xunit;

/// <summary>
/// Requests that depend on other requests: confirming them (one by one or all at once), destroying what others
/// depended on, and the runner applying them in order. Cascading destroys are in <c>CascadingDestroyTests</c>. A <c>test-database</c> depends on a <c>test-server</c>, as a postgres
/// database or a firewall depends on a droplet.
/// </summary>
public class SequencedProvisioningTests : IClassFixture<HighLimitApiFactory>
{
  private const string Route = "/api/requests";

  private readonly HighLimitApiFactory _factory;
  private readonly string _userId = $"user-{Guid.NewGuid():N}";

  public SequencedProvisioningTests(HighLimitApiFactory factory)
  {
    _factory = factory;
    _factory.Terraform.Reset();
  }

  private HttpClient User => _factory.CreateUserClient(_userId, $"{_userId}@example.com");

  [Fact]
  public async Task Confirm_ReturnsConflict_WhileADependencyAwaitsConfirmation()
  {
    var (server, database) = await SeedPair(RequestStatus.AwaitingConfirmation, RequestStatus.AwaitingConfirmation);

    var response = await User.PostAsync($"{Route}/{database.Id}/confirm", null);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    Assert.Equal(
      $"Confirm Test server \"web-1\" ({server.Id}) first, or together with this: it's provisioned before it.",
      problem!.Detail);
    Assert.Equal(RequestStatus.AwaitingConfirmation, (await _factory.FindRequestAsync(database.Id))!.Status);
  }

  [Theory]
  [InlineData(RequestStatus.Queued)]
  [InlineData(RequestStatus.Succeeded)]
  public async Task Confirm_QueuesADependent_OnceItsDependencyIsConfirmed(RequestStatus serverStatus)
  {
    var (_, database) = await SeedPair(serverStatus, RequestStatus.AwaitingConfirmation);

    var response = await User.PostAsync($"{Route}/{database.Id}/confirm", null);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(RequestStatus.Queued, (await _factory.FindRequestAsync(database.Id))!.Status);
  }

  [Fact]
  public async Task ConfirmMany_QueuesTheSet_InDependencyOrder()
  {
    var (server, database) = await SeedPair(RequestStatus.AwaitingConfirmation, RequestStatus.AwaitingConfirmation);

    var response = await User.PostAsJsonAsync(
      $"{Route}/confirm",
      new { requestIds = new[] { database.Id, server.Id } });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var confirmed = await response.Content.ReadFromJsonAsync<List<ProvisioningRequestResponse>>();
    Assert.NotNull(confirmed);
    Assert.Equal([database.Id, server.Id], confirmed.Select(r => r.Id));
    Assert.All(confirmed, r => Assert.Equal(RequestStatus.Queued, r.Status));

    var queued = await ReadQueue(server.Id, database.Id);
    Assert.True(queued.IndexOf(server.Id) < queued.IndexOf(database.Id), "the dependency is queued first");
  }

  [Fact]
  public async Task ConfirmMany_ConfirmsNothing_WhenOneIsNotAwaitingConfirmation()
  {
    var waiting = await _factory.SeedRequestAsync(_userId, RequestStatus.AwaitingConfirmation);
    var queued = await _factory.SeedRequestAsync(_userId, RequestStatus.Queued);

    var response = await User.PostAsJsonAsync(
      $"{Route}/confirm",
      new { requestIds = new[] { waiting.Id, queued.Id } });

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    Assert.Equal(RequestStatus.AwaitingConfirmation, (await _factory.FindRequestAsync(waiting.Id))!.Status);
  }

  [Fact]
  public async Task ConfirmMany_ConfirmsNothing_WhenTheSetWouldGoOverTheLimit()
  {
    await _factory.SeedRequestAsync(_userId, RequestStatus.Succeeded);
    await _factory.SeedRequestAsync(_userId, RequestStatus.Succeeded);
    var (server, database) = await SeedPair(RequestStatus.AwaitingConfirmation, RequestStatus.AwaitingConfirmation);

    var response = await User.PostAsJsonAsync(
      $"{Route}/confirm",
      new { requestIds = new[] { server.Id, database.Id } });

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    Assert.Equal("Active request limit reached", problem!.Title);
    Assert.Contains("confirming these 2 would make 4", problem.Detail);
    Assert.Equal(RequestStatus.AwaitingConfirmation, (await _factory.FindRequestAsync(server.Id))!.Status);
    Assert.Equal(RequestStatus.AwaitingConfirmation, (await _factory.FindRequestAsync(database.Id))!.Status);
  }

  [Fact]
  public async Task ConfirmMany_ReturnsNotFound_ForAnotherUsersRequest()
  {
    var mine = await _factory.SeedRequestAsync(_userId, RequestStatus.AwaitingConfirmation);
    var others = await _factory.SeedRequestAsync("someone-else", RequestStatus.AwaitingConfirmation);

    var response = await User.PostAsJsonAsync(
      $"{Route}/confirm",
      new { requestIds = new[] { mine.Id, others.Id } });

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.Equal(RequestStatus.AwaitingConfirmation, (await _factory.FindRequestAsync(mine.Id))!.Status);
  }

  [Fact]
  public async Task ConfirmMany_ReturnsBadRequest_ForAnEmptySet()
  {
    var response = await User.PostAsJsonAsync($"{Route}/confirm", new { requestIds = Array.Empty<Guid>() });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Destroy_IsAllowed_OnceWhatDependedOnItIsDestroyed()
  {
    var (server, database) = await SeedPair(RequestStatus.Succeeded, RequestStatus.Destroying);
    await Process(database.Id);

    var response = await User.PostAsync($"{Route}/{server.Id}/destroy", null);

    Assert.Null(await _factory.FindRequestAsync(database.Id));
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var body = await response.Content.ReadFromJsonAsync<ProvisioningRequestResponse>();
    Assert.Empty(body!.Dependents);
  }

  [Fact]
  public async Task Destroying_ADependency_UnlinksTheProposalsThatReferencedIt_SoTheyCantBeConfirmed()
  {
    var (server, database) = await SeedPair(RequestStatus.Destroying, RequestStatus.AwaitingConfirmation);

    await Process(server.Id);

    Assert.Null(await _factory.FindRequestAsync(server.Id));
    var view = await User.GetFromJsonAsync<ProvisioningRequestResponse>($"{Route}/{database.Id}");
    Assert.Empty(view!.DependsOn);
    var confirm = await User.PostAsync($"{Route}/{database.Id}/confirm", null);
    Assert.Equal(HttpStatusCode.UnprocessableEntity, confirm.StatusCode);
    var problem = await confirm.Content.ReadFromJsonAsync<ValidationProblemDetails>();
    Assert.Equal(
      [$"'{server.Id}' isn't the request ID of one of your Test server requests."],
      problem!.Errors["allowed_droplet_request_ids[0]"]);
  }

  [Fact]
  public async Task Get_ShowsWhatARequestDependsOn_AndWhatDependsOnIt()
  {
    var (server, database) = await SeedPair(RequestStatus.Succeeded, RequestStatus.Queued);

    var serverView = await User.GetFromJsonAsync<ProvisioningRequestResponse>($"{Route}/{server.Id}");
    var databaseView = await User.GetFromJsonAsync<ProvisioningRequestResponse>($"{Route}/{database.Id}");

    Assert.Empty(serverView!.DependsOn);
    var dependent = Assert.Single(serverView.Dependents);
    Assert.Equal((database.Id, "Test database", "db", RequestStatus.Queued),
      (dependent.Id, dependent.DisplayName, dependent.Name, dependent.Status));
    Assert.Equal(server.Id, Assert.Single(databaseView!.DependsOn).Id);
    Assert.Empty(databaseView.Dependents);
  }

  [Theory]
  [InlineData(RequestStatus.Queued)]
  [InlineData(RequestStatus.Applying)]
  public async Task Runner_HoldsADependent_UntilItsDependencySucceeds(RequestStatus serverStatus)
  {
    var (_, database) = await SeedPair(serverStatus, RequestStatus.Queued);

    await Process(database.Id);

    Assert.Equal(RequestStatus.Queued, (await _factory.FindRequestAsync(database.Id))!.Status);
    Assert.Empty(_factory.Terraform.Invocations);
  }

  [Theory]
  [InlineData(RequestStatus.Failed)]
  [InlineData(RequestStatus.Destroying)]
  public async Task Runner_FailsADependent_WithoutRunning_WhenItsDependencyWontSucceed(RequestStatus serverStatus)
  {
    var (server, database) = await SeedPair(serverStatus, RequestStatus.Queued);

    await Process(database.Id);

    Assert.Equal(RequestStatus.Failed, (await _factory.FindRequestAsync(database.Id))!.Status);
    Assert.Empty(_factory.Terraform.Invocations);
    using var scope = _factory.Services.CreateScope();
    var line = await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobLogLines
      .SingleAsync(x => x.RequestId == database.Id);
    Assert.Equal(
      $"Failed: didn't run, because Test server \"web-1\" ({server.Id}), which it depends on, is {serverStatus}.",
      line.Text);
  }

  [Fact]
  public async Task Runner_AppliesADependent_OnceItsDependencySucceeded()
  {
    var (_, database) = await SeedPair(RequestStatus.Succeeded, RequestStatus.Queued);

    await Process(database.Id);

    Assert.Equal(RequestStatus.Succeeded, (await _factory.FindRequestAsync(database.Id))!.Status);
  }

  [Fact]
  public async Task Runner_ProvisionsASequence_InOrder_EvenWhenTheDependentIsQueuedFirst()
  {
    var (server, database) = await SeedPair(RequestStatus.Queued, RequestStatus.Queued);
    var dispatcher = new JobDispatcher();
    await dispatcher.EnqueueAsync(database.Id);
    await dispatcher.EnqueueAsync(server.Id);

    var runner = new TerraformRunner(
      dispatcher,
      _factory.Services.GetRequiredService<IServiceScopeFactory>(),
      TimeProvider.System,
      NullLogger<TerraformRunner>.Instance);
    await runner.StartAsync(CancellationToken.None);
    try
    {
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
      while ((await _factory.FindRequestAsync(database.Id))!.Status != RequestStatus.Succeeded)
      {
        await Task.Delay(20, timeout.Token);
      }
    }
    finally
    {
      await runner.StopAsync(CancellationToken.None);
      runner.Dispose();
    }

    // The runner's startup recovery also picks up other tests' queued requests; only these two matter here.
    string[] pair = [server.Id.ToString(), database.Id.ToString()];
    var applied = _factory.Terraform.Invocations
      .Where(i => i.Arguments[0] == "apply")
      .Select(i => Path.GetFileName(i.WorkingDirectory))
      .Where(pair.Contains)
      .ToList();
    Assert.Equal([server.Id.ToString(), database.Id.ToString()], applied);
  }

  /// <summary>A <c>test-server</c> and a <c>test-database</c> that depends on it, in the given statuses.</summary>
  private async Task<(ProvisioningRequest Server, ProvisioningRequest Database)> SeedPair(
    RequestStatus serverStatus,
    RequestStatus databaseStatus)
  {
    var server = await _factory.SeedServerAsync(_userId, serverStatus);
    var database = await _factory.SeedRequestAsync(
      _userId,
      databaseStatus,
      type: "test-database",
      variables: $$"""{"name":"db","allowed_droplet_request_ids":["{{server.Id}}"],"allowed_ip_addresses":[]}""");
    await _factory.AddDependencyAsync(database.Id, server.Id);
    return (server, database);
  }

  private async Task Process(Guid requestId)
  {
    using var scope = _factory.Services.CreateScope();
    var processor = scope.ServiceProvider.GetRequiredService<ITerraformJobProcessor>();
    await processor.RunAsync(requestId, CancellationToken.None);
  }

  /// <summary>
  /// Reads the runner's queue until both requests have turned up; the background service is off in tests, so the
  /// queue holds everything this class queued.
  /// </summary>
  private async Task<List<Guid>> ReadQueue(params Guid[] expected)
  {
    var read = new List<Guid>();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await foreach (var id in _factory.Services.GetRequiredService<IJobDispatcher>().ReadAllAsync(timeout.Token))
    {
      read.Add(id);
      if (expected.All(read.Contains))
      {
        break;
      }
    }

    return read;
  }
}
