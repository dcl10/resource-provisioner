namespace ResourceProvisioner.Api.Tests;

using Data;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Models;
using Services.Provisioning;
using Services.Requests;
using Xunit;

/// <summary>
/// <see cref="RequestDeleter"/> against the real <see cref="TerraformStateStore"/>, so the request and its Terraform
/// state are seen to go together, in one transaction, or not at all.
/// </summary>
public class RequestDeleterTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private readonly string _ownerId = $"owner-{Guid.NewGuid():N}";

  [Fact]
  public async Task DeleteDestroyedAsync_DeletesTheRequestWithEverythingStoredForIt_AndItsState()
  {
    var request = await factory.SeedRequestAsync(
      _ownerId,
      RequestStatus.Destroying,
      outputs: """{"id":"123"}""",
      sensitiveOutputs: """{"root_password":"secret"}""");
    var dependency = await factory.SeedRequestAsync(_ownerId, RequestStatus.Succeeded);
    await factory.AddDependencyAsync(request.Id, dependency.Id);
    var proposal = await factory.SeedRequestAsync(_ownerId, RequestStatus.AwaitingConfirmation);
    await factory.AddDependencyAsync(proposal.Id, request.Id);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.JobLogLines.Add(new JobLogLine
    {
      RequestId = request.Id, At = DateTimeOffset.UtcNow, Stream = LogSource.System, Text = "Destroyed.",
    });
    await db.SaveChangesAsync();
    await TerraformStateStoreTests.AddState(db, request.Id);
    var (deleter, store) = Deleter(db);

    await deleter.DeleteDestroyedAsync(request.Id, CancellationToken.None);

    Assert.False(await db.ProvisioningRequests.AnyAsync(x => x.Id == request.Id));
    Assert.False(await db.RequestSensitiveOutputs.AnyAsync(x => x.RequestId == request.Id));
    Assert.False(await db.JobLogLines.AnyAsync(x => x.RequestId == request.Id));
    Assert.False(await db.RequestDependencies.AnyAsync(x => x.RequestId == request.Id || x.DependsOnId == request.Id));
    Assert.False(await store.HasStateAsync(request.Id, CancellationToken.None));
    Assert.True(await db.ProvisioningRequests.AnyAsync(x => x.Id == dependency.Id));
    Assert.True(await db.ProvisioningRequests.AnyAsync(x => x.Id == proposal.Id));
  }

  [Theory]
  [InlineData(RequestStatus.Succeeded)]
  [InlineData(RequestStatus.Failed)]
  public async Task DeleteDestroyedAsync_KeepsEverything_WhenTheRequestIsntDestroying(RequestStatus status)
  {
    // The state is deleted before the request, so this shows the two are one transaction.
    var request = await factory.SeedRequestAsync(_ownerId, status, sensitiveOutputs: """{"root_password":"secret"}""");
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await TerraformStateStoreTests.AddState(db, request.Id);
    var (deleter, store) = Deleter(db);

    await Assert.ThrowsAsync<RequestConflictException>(
      () => deleter.DeleteDestroyedAsync(request.Id, CancellationToken.None));

    Assert.True(await db.ProvisioningRequests.AnyAsync(x => x.Id == request.Id && x.Status == status));
    Assert.True(await db.RequestSensitiveOutputs.AnyAsync(x => x.RequestId == request.Id));
    Assert.True(await store.HasStateAsync(request.Id, CancellationToken.None));
  }

  [Theory]
  [InlineData(RequestStatus.Queued)]
  [InlineData(RequestStatus.Succeeded)]
  [InlineData(RequestStatus.Failed)]
  [InlineData(RequestStatus.Destroying)]
  public async Task DeleteDestroyedAsync_KeepsEverything_WhileSomethingThatMayExistDependsOnIt(RequestStatus status)
  {
    var request = await factory.SeedRequestAsync(_ownerId, RequestStatus.Destroying);
    var dependent = await factory.SeedRequestAsync(_ownerId, status);
    await factory.AddDependencyAsync(dependent.Id, request.Id);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await TerraformStateStoreTests.AddState(db, request.Id);
    var (deleter, store) = Deleter(db);

    await Assert.ThrowsAsync<RequestConflictException>(
      () => deleter.DeleteDestroyedAsync(request.Id, CancellationToken.None));

    Assert.True(await db.ProvisioningRequests.AnyAsync(x => x.Id == request.Id));
    Assert.True(await db.RequestDependencies.AnyAsync(x => x.DependsOnId == request.Id));
    Assert.True(await store.HasStateAsync(request.Id, CancellationToken.None));
  }

  [Fact]
  public async Task DeleteCancelledAsync_DeletesAProjectsRequestAndItsProject_AndUnlinksItsProposals()
  {
    var project = await factory.SeedProjectAsync(_ownerId, RequestStatus.AwaitingConfirmation);
    var proposal = await factory.SeedRequestAsync(_ownerId, RequestStatus.AwaitingConfirmation, projectId: project.Id);
    await factory.AddDependencyAsync(proposal.Id, project.ProjectRequestId);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var (deleter, _) = Deleter(db);

    await deleter.DeleteCancelledAsync(project.ProjectRequestId, CancellationToken.None);

    Assert.False(await db.ProvisioningRequests.AnyAsync(x => x.Id == project.ProjectRequestId));
    Assert.False(await db.Projects.AnyAsync(x => x.Id == project.Id));
    var kept = await db.ProvisioningRequests.AsNoTracking().SingleAsync(x => x.Id == proposal.Id);
    Assert.Null(kept.ProjectId);
    Assert.False(await db.RequestDependencies.AnyAsync(x => x.RequestId == proposal.Id));
  }

  [Fact]
  public async Task DeleteCancelledAsync_LeavesOtherRequestsState_Alone()
  {
    var request = await factory.SeedRequestAsync(_ownerId, RequestStatus.AwaitingConfirmation);
    var other = Guid.NewGuid();
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await TerraformStateStoreTests.AddState(db, other);
    var (deleter, store) = Deleter(db);

    await deleter.DeleteCancelledAsync(request.Id, CancellationToken.None);

    Assert.False(await db.ProvisioningRequests.AnyAsync(x => x.Id == request.Id));
    Assert.True(await store.HasStateAsync(other, CancellationToken.None));
  }

  [Theory]
  [InlineData(RequestStatus.Queued)]
  [InlineData(RequestStatus.Destroying)]
  public async Task DeleteCancelledAsync_Refuses_WhenTheRequestIsNoLongerAwaitingConfirmation(RequestStatus status)
  {
    var request = await factory.SeedRequestAsync(_ownerId, status);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var (deleter, _) = Deleter(db);

    await Assert.ThrowsAsync<RequestConflictException>(
      () => deleter.DeleteCancelledAsync(request.Id, CancellationToken.None));

    Assert.True(await db.ProvisioningRequests.AnyAsync(x => x.Id == request.Id));
  }

  [Fact]
  public async Task DeleteCancelledAsync_JoinsTheCallersTransaction()
  {
    var request = await factory.SeedRequestAsync(_ownerId, RequestStatus.AwaitingConfirmation);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var (deleter, _) = Deleter(db);

    await using (var transaction = await db.Database.BeginTransactionAsync())
    {
      await deleter.DeleteCancelledAsync(request.Id, CancellationToken.None);
      await transaction.RollbackAsync();
    }

    Assert.True(await db.ProvisioningRequests.AnyAsync(x => x.Id == request.Id));
  }

  private static (RequestDeleter Deleter, TerraformStateStore Store) Deleter(AppDbContext db)
  {
    var store = new TerraformStateStore(db);
    return (new RequestDeleter(db, store, NullLogger<RequestDeleter>.Instance), store);
  }
}
