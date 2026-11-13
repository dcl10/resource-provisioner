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
/// Destroying a request destroys everything that depends on it, and deleting a project destroys everything in it:
/// the plan, the destroy itself (cancelling proposals, refusing while something is in the runner's hands), the runner
/// tearing down in order, and what happens when one destroy in the middle fails. A <c>test-database</c> depends on a
/// <c>test-server</c>, and both on the project they're in.
/// </summary>
public class CascadingDestroyTests : IClassFixture<HighLimitApiFactory>
{
  private const string Requests = "/api/requests";
  private const string Projects = "/api/projects";

  private readonly HighLimitApiFactory _factory;
  private readonly string _userId = $"user-{Guid.NewGuid():N}";

  public CascadingDestroyTests(HighLimitApiFactory factory)
  {
    _factory = factory;
    _factory.Terraform.Reset();
  }

  private HttpClient User => _factory.CreateUserClient(_userId, $"{_userId}@example.com");

  [Theory]
  [InlineData(RequestStatus.Succeeded)]
  [InlineData(RequestStatus.Failed)]
  [InlineData(RequestStatus.DestroyFailed)]
  public async Task Destroy_MovesItAndWhatDependsOnItToDestroying_RecordingEachOnesStatus(RequestStatus status)
  {
    var tree = await SeedTree(databaseStatus: status);

    var response = await User.PostAsync($"{Requests}/{tree.Server.Id}/destroy", null);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var server = await _factory.FindRequestAsync(tree.Server.Id);
    var database = await _factory.FindRequestAsync(tree.Database.Id);
    Assert.Equal((RequestStatus.Destroying, (RequestStatus?)RequestStatus.Succeeded),
      (server!.Status, server.StatusBeforeDestroy));
    Assert.Equal((RequestStatus.Destroying, (RequestStatus?)status), (database!.Status, database.StatusBeforeDestroy));

    // What it depends on is left alone.
    Assert.Equal(RequestStatus.Succeeded, (await _factory.FindRequestAsync(tree.Project.ProjectRequestId))!.Status);
    await ReadQueue(tree.Server.Id, tree.Database.Id);
  }

  [Fact]
  public async Task Destroy_CancelsTheProposalsThatDependOnIt()
  {
    var tree = await SeedTree(databaseStatus: RequestStatus.AwaitingConfirmation);

    var response = await User.PostAsync($"{Requests}/{tree.Server.Id}/destroy", null);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Null(await _factory.FindRequestAsync(tree.Database.Id));
    Assert.Equal(RequestStatus.Destroying, (await _factory.FindRequestAsync(tree.Server.Id))!.Status);
    var body = await response.Content.ReadFromJsonAsync<ProvisioningRequestResponse>();
    Assert.Empty(body!.Dependents);
  }

  [Theory]
  [InlineData(RequestStatus.Queued)]
  [InlineData(RequestStatus.Applying)]
  [InlineData(RequestStatus.Destroying)]
  public async Task Destroy_ReturnsConflictAndChangesNothing_WhileSomethingThatDependsOnItIsInTheRunnersHands(
    RequestStatus status)
  {
    // The database depends on the project only through the server and its membership: the closure is transitive.
    var tree = await SeedTree(databaseStatus: status);
    var proposal = await SeedProposalIn(tree.Project);

    var response = await User.PostAsync($"{Requests}/{tree.Project.ProjectRequestId}/destroy", null);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    Assert.Equal(
      $"Wait for Test database \"db\" ({tree.Database.Id}) to finish, then try again: it depends on this request.",
      problem!.Detail);
    Assert.Equal(RequestStatus.Succeeded, (await _factory.FindRequestAsync(tree.Project.ProjectRequestId))!.Status);
    Assert.Equal(RequestStatus.Succeeded, (await _factory.FindRequestAsync(tree.Server.Id))!.Status);
    Assert.NotNull(await _factory.FindRequestAsync(proposal.Id));
  }

  [Fact]
  public async Task Destroy_ReturnsNotFound_ForAnotherUsersRequest_AndIsAllowedForAnAdmin()
  {
    var tree = await SeedTree();
    var someoneElse = _factory.CreateUserClient($"other-{Guid.NewGuid():N}");

    var refused = await someoneElse.PostAsync($"{Requests}/{tree.Server.Id}/destroy", null);
    var allowed = await _factory.CreateAdminClient().PostAsync($"{Requests}/{tree.Server.Id}/destroy", null);

    Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
    Assert.Equal(RequestStatus.Destroying, (await _factory.FindRequestAsync(tree.Database.Id))!.Status);
  }

  [Fact]
  public async Task DestroyPlan_ListsWhatWouldBeDestroyedInTeardownOrder_AndWhatWouldBeCancelled_ChangingNothing()
  {
    var tree = await SeedTree();
    var proposal = await SeedProposalIn(tree.Project);

    var plan = await User.GetFromJsonAsync<DestroyPlanResponse>(
      $"{Requests}/{tree.Project.ProjectRequestId}/destroy-plan");

    Assert.NotNull(plan);
    Assert.True(plan.CanDestroy);
    Assert.Null(plan.Reason);
    Assert.Equal([tree.Database.Id, tree.Server.Id, tree.Project.ProjectRequestId], plan.Destroy.Select(l => l.Id));
    Assert.Equal(
      ("Test database", "db", RequestStatus.Succeeded),
      (plan.Destroy[0].DisplayName, plan.Destroy[0].Name, plan.Destroy[0].Status));
    Assert.Equal(proposal.Id, Assert.Single(plan.Cancel).Id);
    Assert.Empty(plan.WaitFor);
    Assert.Equal(RequestStatus.Succeeded, (await _factory.FindRequestAsync(tree.Server.Id))!.Status);
    Assert.NotNull(await _factory.FindRequestAsync(proposal.Id));
  }

  [Fact]
  public async Task DestroyPlan_SaysWhyAndWhatToWaitFor_WhenTheDestroyWouldBeRefused()
  {
    var tree = await SeedTree(databaseStatus: RequestStatus.Applying);

    var plan = await User.GetFromJsonAsync<DestroyPlanResponse>($"{Requests}/{tree.Server.Id}/destroy-plan");

    Assert.NotNull(plan);
    Assert.False(plan.CanDestroy);
    Assert.Equal(
      $"Wait for Test database \"db\" ({tree.Database.Id}) to finish, then try again: it depends on this request.",
      plan.Reason);
    Assert.Equal(
      (tree.Database.Id, RequestStatus.Applying),
      (Assert.Single(plan.WaitFor).Id, plan.WaitFor[0].Status));
  }

  [Fact]
  public async Task DestroyPlan_SaysWhy_WhenTheRequestItselfCantBeDestroyed()
  {
    var request = await _factory.SeedRequestAsync(_userId, RequestStatus.Queued);

    var plan = await User.GetFromJsonAsync<DestroyPlanResponse>($"{Requests}/{request.Id}/destroy-plan");

    Assert.False(plan!.CanDestroy);
    Assert.Equal("This request is Queued, so it can't be destroyed.", plan.Reason);
  }

  [Fact]
  public async Task DestroyPlan_ReturnsNotFound_ForAnotherUsersRequest_AndTheOwnersPlanForAnAdmin()
  {
    var tree = await SeedTree();
    var url = $"{Requests}/{tree.Server.Id}/destroy-plan";

    var refused = await _factory.CreateUserClient($"other-{Guid.NewGuid():N}").GetAsync(url);
    var plan = await _factory.CreateAdminClient().GetFromJsonAsync<DestroyPlanResponse>(url);

    Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    Assert.Equal([tree.Database.Id, tree.Server.Id], plan!.Destroy.Select(l => l.Id));
  }

  [Fact]
  public async Task Runner_TearsDownInOrder_ThenTheProjectIsGone_WhenTheProjectIsDeleted()
  {
    var tree = await SeedTree();
    var proposal = await SeedProposalIn(tree.Project);

    var response = await User.DeleteAsync($"{Projects}/{tree.Project.Id}");

    Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    var project = await response.Content.ReadFromJsonAsync<ProjectDetailResponse>();
    Assert.Equal(RequestStatus.Destroying, project!.Status);
    Assert.Null(await _factory.FindRequestAsync(proposal.Id));
    Assert.All(project.Resources, r => Assert.Equal(RequestStatus.Destroying, r.Status));

    // Queued the wrong way round: the runner still destroys what depends on something first.
    await RunUntilDeleted(
      [tree.Project.ProjectRequestId, tree.Server.Id, tree.Database.Id],
      tree.Project.ProjectRequestId);

    Assert.Equal([tree.Database.Id, tree.Server.Id, tree.Project.ProjectRequestId], DestroyOrder(tree));
    Assert.Null(await _factory.FindRequestAsync(tree.Server.Id));
    Assert.Null(await _factory.FindRequestAsync(tree.Database.Id));
    Assert.Equal(HttpStatusCode.NotFound, (await User.GetAsync($"{Projects}/{tree.Project.Id}")).StatusCode);
  }

  [Fact]
  public async Task Runner_StopsTheCascade_WhenADestroyFails_AndARetryResumesIt()
  {
    var tree = await SeedTree();
    await User.DeleteAsync($"{Projects}/{tree.Project.Id}");
    _factory.Terraform.Script(tree.Server.Id, "destroy", 1, (LogSource.Stderr, "Error: droplet is locked"));

    await RunUntil(
      [tree.Project.ProjectRequestId, tree.Server.Id, tree.Database.Id],
      async () => (await _factory.FindRequestAsync(tree.Project.ProjectRequestId))!.Status != RequestStatus.Destroying);

    // The database went; the server failed; the project, which was waiting on it, is back and untouched.
    Assert.Null(await _factory.FindRequestAsync(tree.Database.Id));
    Assert.Equal(RequestStatus.DestroyFailed, (await _factory.FindRequestAsync(tree.Server.Id))!.Status);
    var projectRequest = await User.GetFromJsonAsync<ProvisioningRequestResponse>(
      $"{Requests}/{tree.Project.ProjectRequestId}");
    Assert.Equal(RequestStatus.Succeeded, projectRequest!.Status);
    Assert.Equal(
      (tree.Server.Id, RequestStatus.DestroyFailed),
      (projectRequest.DestroyStoppedBy!.Id, projectRequest.DestroyStoppedBy.Status));
    Assert.DoesNotContain(tree.Project.ProjectRequestId, DestroyOrder(tree));
    Assert.True(await _factory.TerraformState.HasStateAsync(tree.Project.ProjectRequestId, CancellationToken.None));
    Assert.True(await _factory.TerraformState.HasStateAsync(tree.Server.Id, CancellationToken.None));
    Assert.Equal(
      $"Destroy stopped: Test server \"web-1\" ({tree.Server.Id}) failed to destroy.",
      (await Lines(tree.Project.ProjectRequestId))[^1]);

    // The retry takes up the server where it failed.
    _factory.Terraform.Reset();
    var retry = await User.DeleteAsync($"{Projects}/{tree.Project.Id}");
    Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
    Assert.Equal(
      (RequestStatus.Destroying, (RequestStatus?)RequestStatus.DestroyFailed),
      ((await _factory.FindRequestAsync(tree.Server.Id))!.Status,
        (await _factory.FindRequestAsync(tree.Server.Id))!.StatusBeforeDestroy));
    var stopped = await User.GetFromJsonAsync<ProvisioningRequestResponse>(
      $"{Requests}/{tree.Project.ProjectRequestId}");
    Assert.Null(stopped!.DestroyStoppedBy);

    await RunUntilDeleted([tree.Project.ProjectRequestId, tree.Server.Id], tree.Project.ProjectRequestId);

    Assert.Equal([tree.Server.Id, tree.Project.ProjectRequestId], DestroyOrder(tree));
    Assert.Null(await _factory.FindRequestAsync(tree.Server.Id));
  }

  [Fact]
  public async Task DeleteProject_ReturnsConflictAndChangesNothing_WhileSomethingInItIsInTheRunnersHands()
  {
    var tree = await SeedTree(databaseStatus: RequestStatus.Queued);

    var response = await User.DeleteAsync($"{Projects}/{tree.Project.Id}");

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    Assert.Equal(RequestStatus.Succeeded, (await _factory.FindRequestAsync(tree.Project.ProjectRequestId))!.Status);
    Assert.Equal(RequestStatus.Succeeded, (await _factory.FindRequestAsync(tree.Server.Id))!.Status);
  }

  [Fact]
  public async Task DeleteProject_ReturnsNotFound_ForAnotherUsersProject_AndIsAllowedForAnAdmin()
  {
    var tree = await SeedTree();

    var refused = await _factory.CreateUserClient($"other-{Guid.NewGuid():N}")
      .DeleteAsync($"{Projects}/{tree.Project.Id}");
    var allowed = await _factory.CreateAdminClient().DeleteAsync($"{Projects}/{tree.Project.Id}");

    Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    Assert.Equal(HttpStatusCode.Accepted, allowed.StatusCode);
    Assert.Equal(RequestStatus.Destroying, (await _factory.FindRequestAsync(tree.Database.Id))!.Status);
  }

  /// <summary>
  /// A project, a server in it and a database in it that references the server, each with Terraform state and saved
  /// tfvars, as proposing and applying them would leave them.
  /// </summary>
  private async Task<Tree> SeedTree(
    RequestStatus serverStatus = RequestStatus.Succeeded,
    RequestStatus databaseStatus = RequestStatus.Succeeded)
  {
    var project = await _factory.SeedProjectAsync(_userId);
    var server = await _factory.SeedRequestAsync(
      _userId,
      serverStatus,
      projectId: project.Id,
      terraformVariables: RequestSeeding.ServerVariables);
    await _factory.AddDependencyAsync(server.Id, project.ProjectRequestId);
    var database = await _factory.SeedRequestAsync(
      _userId,
      databaseStatus,
      type: "test-database",
      variables: $$"""{"name":"db","allowed_droplet_request_ids":["{{server.Id}}"],"allowed_ip_addresses":[]}""",
      terraformVariables: "{}",
      projectId: project.Id);
    await _factory.AddDependencyAsync(database.Id, project.ProjectRequestId);
    await _factory.AddDependencyAsync(database.Id, server.Id);

    // The fixture's project request has no saved tfvars; a real apply would have saved them.
    using (var scope = _factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      await db.ProvisioningRequests.Where(r => r.Id == project.ProjectRequestId)
        .ExecuteUpdateAsync(s => s.SetProperty(r => r.TerraformVariables, "{}"));
    }

    foreach (var id in new[] { project.ProjectRequestId, server.Id, database.Id })
    {
      _factory.TerraformState.Add(id);
    }

    return new Tree(project, server, database);
  }

  private Task<ProvisioningRequest> SeedProposalIn(Project project) =>
    _factory.SeedRequestAsync(_userId, RequestStatus.AwaitingConfirmation, projectId: project.Id);

  /// <summary>The tree's requests in the order <c>terraform destroy</c> ran for them.</summary>
  private List<Guid> DestroyOrder(Tree tree)
  {
    string[] ids = [tree.Project.ProjectRequestId.ToString(), tree.Server.Id.ToString(), tree.Database.Id.ToString()];
    return
    [
      .. _factory.Terraform.Invocations
        .Where(i => i.Arguments[0] == "destroy")
        .Select(i => Path.GetFileName(i.WorkingDirectory))
        .Where(ids.Contains)
        .Select(Guid.Parse),
    ];
  }

  private async Task<List<string>> Lines(Guid requestId)
  {
    using var scope = _factory.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobLogLines
      .Where(x => x.RequestId == requestId)
      .OrderBy(x => x.Id)
      .Select(x => x.Text)
      .ToListAsync();
  }

  private Task RunUntilDeleted(Guid[] queue, Guid last) =>
    RunUntil(queue, async () => await _factory.FindRequestAsync(last) is null);

  /// <summary>
  /// Runs the real runner, with <paramref name="queue"/> queued in that order, until <paramref name="done"/>. Its
  /// startup recovery also picks up whatever else is pending in this class's database; tests look only at their own.
  /// </summary>
  private async Task RunUntil(Guid[] queue, Func<Task<bool>> done)
  {
    var dispatcher = new JobDispatcher();
    foreach (var id in queue)
    {
      await dispatcher.EnqueueAsync(id);
    }

    var runner = new TerraformRunner(
      dispatcher,
      _factory.Services.GetRequiredService<IServiceScopeFactory>(),
      TimeProvider.System,
      NullLogger<TerraformRunner>.Instance);
    await runner.StartAsync(CancellationToken.None);
    try
    {
      using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
      while (!await done())
      {
        await Task.Delay(20, timeout.Token);
      }
    }
    finally
    {
      await runner.StopAsync(CancellationToken.None);
      runner.Dispose();
    }
  }

  /// <summary>Reads the app's queue until all the requests have turned up.</summary>
  private async Task ReadQueue(params Guid[] expected)
  {
    var read = new HashSet<Guid>();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    await foreach (var id in _factory.Services.GetRequiredService<IJobDispatcher>().ReadAllAsync(timeout.Token))
    {
      read.Add(id);
      if (expected.All(read.Contains))
      {
        return;
      }
    }
  }

  private sealed record Tree(Project Project, ProvisioningRequest Server, ProvisioningRequest Database);
}
