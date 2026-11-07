namespace ResourceProvisioner.Api.Tests;

using Data;
using Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Services.Provisioning;
using Xunit;

/// <summary>A test host with no DigitalOcean token configured.</summary>
public class NoTokenApiFactory : ApiFactory
{
  /// <inheritdoc />
  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    base.ConfigureWebHost(builder);
    builder.ConfigureAppConfiguration(config =>
      config.AddInMemoryCollection(new Dictionary<string, string?> { ["DigitalOcean:Token"] = "" }));
  }
}

/// <summary>The runner's background service: startup recovery and taking jobs off the queue.</summary>
public class TerraformRunnerTests : IClassFixture<ApiFactory>, IClassFixture<NoTokenApiFactory>
{
  private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

  private readonly ApiFactory _factory;
  private readonly NoTokenApiFactory _noToken;

  public TerraformRunnerTests(ApiFactory factory, NoTokenApiFactory noToken)
  {
    _factory = factory;
    _noToken = noToken;
    _factory.Terraform.Reset();
  }

  [Fact]
  public async Task Runner_AppliesQueuedJobs_OneAtATime()
  {
    var first = await _factory.SeedRequestAsync("owner-a", RequestStatus.Queued);
    var second = await _factory.SeedRequestAsync("owner-a", RequestStatus.Queued);
    var dispatcher = new JobDispatcher();
    await dispatcher.EnqueueAsync(first.Id);
    await dispatcher.EnqueueAsync(second.Id);

    await using (await Start(dispatcher))
    {
      await WaitFor(second.Id, RequestStatus.Succeeded);
    }

    Assert.Equal(RequestStatus.Succeeded, (await _factory.FindRequestAsync(first.Id))!.Status);

    // Each job's four commands ran together: the second job didn't start until the first had finished.
    var workspaces = _factory.Terraform.Invocations.Select(i => Path.GetFileName(i.WorkingDirectory)).ToList();
    Assert.Equal(
      [.. Enumerable.Repeat(first.Id.ToString(), 4), .. Enumerable.Repeat(second.Id.ToString(), 4)],
      workspaces.Where(w => w == first.Id.ToString() || w == second.Id.ToString()));
  }

  [Fact]
  public async Task Runner_RequeuesQueuedRequests_AtStartup()
  {
    var request = await _factory.SeedRequestAsync("owner-b", RequestStatus.Queued);

    await using (await Start(new JobDispatcher()))
    {
      await WaitFor(request.Id, RequestStatus.Succeeded);
    }
  }

  [Fact]
  public async Task Runner_RequeuesDestroyingRequests_AtStartup()
  {
    var request = await _factory.SeedRequestAsync("owner-b", RequestStatus.Destroying);
    var workspace = Path.Combine(_factory.WorkspacesRoot, request.Id.ToString());
    Directory.CreateDirectory(workspace);
    await File.WriteAllTextAsync(Path.Combine(workspace, TerraformWorkspace.StateFile), "{}");

    await using (await Start(new JobDispatcher()))
    {
      await WaitFor(request.Id, RequestStatus.Destroyed);
    }

    Assert.Contains(_factory.Terraform.Invocations, i => i.Arguments[0] == "destroy");
  }

  [Fact]
  public async Task Runner_FailsInterruptedApplies_AtStartup()
  {
    var request = await _factory.SeedRequestAsync("owner-c", RequestStatus.Applying);

    await using (await Start(new JobDispatcher()))
    {
      await WaitFor(request.Id, RequestStatus.Failed);
    }

    using var scope = _factory.Services.CreateScope();
    var lines = await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobLogLines
      .Where(x => x.RequestId == request.Id)
      .ToListAsync();
    Assert.Equal(TerraformRunner.InterruptedMessage, Assert.Single(lines).Text);
  }

  [Fact]
  public async Task Runner_FailsJob_WhenDigitalOceanTokenIsMissing()
  {
    var request = await _noToken.SeedRequestAsync("owner-d", RequestStatus.Queued);

    await using (await Start(new JobDispatcher(), _noToken))
    {
      await WaitFor(request.Id, RequestStatus.Failed, _noToken);
    }

    Assert.Empty(_noToken.Terraform.Invocations);
  }

  private Task<RunningRunner> Start(JobDispatcher dispatcher) => Start(dispatcher, _factory);

  private static async Task<RunningRunner> Start(JobDispatcher dispatcher, ApiFactory factory)
  {
    var runner = new TerraformRunner(
      dispatcher,
      factory.Services.GetRequiredService<IServiceScopeFactory>(),
      TimeProvider.System,
      NullLogger<TerraformRunner>.Instance);
    await runner.StartAsync(CancellationToken.None);
    return new RunningRunner(runner);
  }

  private Task WaitFor(Guid id, RequestStatus status) => WaitFor(id, status, _factory);

  private static async Task WaitFor(Guid id, RequestStatus status, ApiFactory factory)
  {
    using var cts = new CancellationTokenSource(Timeout);
    while ((await factory.FindRequestAsync(id))!.Status != status)
    {
      await Task.Delay(20, cts.Token);
    }
  }

  private sealed class RunningRunner(TerraformRunner runner) : IAsyncDisposable
  {
    public async ValueTask DisposeAsync()
    {
      await runner.StopAsync(CancellationToken.None);
      runner.Dispose();
    }
  }
}
