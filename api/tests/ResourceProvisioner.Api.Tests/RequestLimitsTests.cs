namespace ResourceProvisioner.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using Fixtures;
using Microsoft.AspNetCore.Mvc;
using Models;
using Xunit;

/// <summary>
/// <c>Limits:MaxActiveRequestsPerUser</c> (3) and <c>Limits:MaxActiveRequestsTotal</c> (10) on confirm. Each test
/// gets its own host and database, since the global limit counts every request in it.
/// </summary>
public sealed class RequestLimitsTests : IDisposable
{
  private readonly ApiFactory _factory = new();

  public void Dispose() => _factory.Dispose();

  [Fact]
  public async Task Confirm_ReturnsConflict_WhenUserHasMaxActiveRequests()
  {
    await _factory.SeedRequestAsync("user-1", RequestStatus.Queued);
    await _factory.SeedRequestAsync("user-1", RequestStatus.Applying);
    await _factory.SeedRequestAsync("user-1", RequestStatus.Destroying);
    var request = await _factory.SeedRequestAsync("user-1", RequestStatus.AwaitingConfirmation);

    var response = await Confirm("user-1", request.Id);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    Assert.Equal("Active request limit reached", problem!.Title);
    Assert.Equal(
      "You already have 3 active requests, and each user can have at most 3. Destroy one before confirming another.",
      problem.Detail);
    Assert.Equal(RequestStatus.AwaitingConfirmation, (await _factory.FindRequestAsync(request.Id))!.Status);
  }

  [Fact]
  public async Task Confirm_CountsSucceededAsActive()
  {
    for (var i = 0; i < 3; i++)
    {
      await _factory.SeedRequestAsync("user-1", RequestStatus.Succeeded);
    }

    var request = await _factory.SeedRequestAsync("user-1", RequestStatus.AwaitingConfirmation);

    Assert.Equal(HttpStatusCode.Conflict, (await Confirm("user-1", request.Id)).StatusCode);
  }

  [Fact]
  public async Task Confirm_IgnoresInactiveRequests_WhenCountingTowardsLimits()
  {
    foreach (var status in new[]
             {
               RequestStatus.AwaitingConfirmation, RequestStatus.Failed, RequestStatus.Destroyed,
               RequestStatus.Cancelled,
             })
    {
      for (var i = 0; i < 3; i++)
      {
        await _factory.SeedRequestAsync("user-1", status);
      }
    }

    await _factory.SeedRequestAsync("user-1", RequestStatus.Succeeded);
    await _factory.SeedRequestAsync("user-1", RequestStatus.Succeeded);
    var request = await _factory.SeedRequestAsync("user-1", RequestStatus.AwaitingConfirmation);

    var response = await Confirm("user-1", request.Id);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Confirm_ReturnsConflict_WhenTotalActiveRequestsReachLimit()
  {
    for (var i = 0; i < 10; i++)
    {
      await _factory.SeedRequestAsync($"other-{i % 5}", RequestStatus.Succeeded);
    }

    var request = await _factory.SeedRequestAsync("user-1", RequestStatus.AwaitingConfirmation);

    var response = await Confirm("user-1", request.Id);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    Assert.Equal(
      "The app already has 10 active requests across all users, its limit of 10. Try again once some have been " +
      "destroyed.",
      problem!.Detail);
  }

  [Fact]
  public async Task Confirm_AllowsLastSlot_WhenOneBelowEachLimit()
  {
    for (var i = 0; i < 7; i++)
    {
      await _factory.SeedRequestAsync($"other-{i % 3}", RequestStatus.Succeeded);
    }

    await _factory.SeedRequestAsync("user-1", RequestStatus.Queued);
    await _factory.SeedRequestAsync("user-1", RequestStatus.Queued);
    var request = await _factory.SeedRequestAsync("user-1", RequestStatus.AwaitingConfirmation);

    Assert.Equal(HttpStatusCode.OK, (await Confirm("user-1", request.Id)).StatusCode);

    var next = await _factory.SeedRequestAsync("user-2", RequestStatus.AwaitingConfirmation);
    Assert.Equal(HttpStatusCode.Conflict, (await Confirm("user-2", next.Id)).StatusCode);
  }

  private Task<HttpResponseMessage> Confirm(string userId, Guid id) =>
    _factory.CreateUserClient(userId).PostAsync($"/api/requests/{id}/confirm", null);
}
