namespace ResourceProvisioner.Api.Tests;

using System.Globalization;
using System.Net;
using Data;
using Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Xunit;

/// <summary><c>GET /api/requests/{id}/logs</c>, the server-sent event stream of a request's Terraform log.</summary>
public class RequestLogsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private readonly string _userId = $"user-{Guid.NewGuid():N}";

  private HttpClient User => factory.CreateUserClient(_userId, $"{_userId}@example.com");

  [Fact]
  public async Task Logs_ReturnsUnauthorized_WithoutCredentials()
  {
    var response = await factory.CreateAnonymousClient().GetAsync($"/api/requests/{Guid.NewGuid()}/logs");

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Logs_ReturnsNotFound_ForAnotherUsersRequest()
  {
    var request = await factory.SeedRequestAsync("someone-else", RequestStatus.Succeeded);

    var response = await User.GetAsync($"/api/requests/{request.Id}/logs");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Logs_ReturnsNotFound_WhenRequestDoesNotExist()
  {
    var response = await User.GetAsync($"/api/requests/{Guid.NewGuid()}/logs");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Logs_StreamsStatusLinesAndEnd_ForFinishedRequest()
  {
    var request = await factory.SeedRequestAsync(_userId, RequestStatus.Succeeded);
    var ids = await AddLines(request.Id, (LogSource.System, "$ terraform init"), (LogSource.Stdout, "Created."));

    var response = await User.GetAsync($"/api/requests/{request.Id}/logs");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    var events = Sse.Parse(await response.Content.ReadAsStringAsync());
    Assert.Equal(["status", "log", "log", "end"], events.Select(e => e.Type));
    Assert.Equal("Succeeded", events[0].Data["status"]!.GetValue<string>());
    Assert.Equal(ids[1].ToString(CultureInfo.InvariantCulture), events[2].Id);
    Assert.Equal("Created.", events[2].Data["text"]!.GetValue<string>());
    Assert.Equal("Stdout", events[2].Data["stream"]!.GetValue<string>());
    Assert.Equal("Succeeded", events[3].Data["status"]!.GetValue<string>());
  }

  [Fact]
  public async Task Logs_ResumesAfterLastEventId()
  {
    var request = await factory.SeedRequestAsync(_userId, RequestStatus.Failed);
    var ids = await AddLines(request.Id, (LogSource.Stdout, "one"), (LogSource.Stdout, "two"));
    var client = User;
    client.DefaultRequestHeaders.Add("Last-Event-ID", ids[0].ToString(CultureInfo.InvariantCulture));

    var events = Sse.Parse(await client.GetStringAsync($"/api/requests/{request.Id}/logs"));

    Assert.Equal(["two"], events.Where(e => e.Type == "log").Select(e => e.Data["text"]!.GetValue<string>()));
  }

  [Fact]
  public async Task Logs_StreamsAnyRequest_ForAdmin()
  {
    var request = await factory.SeedRequestAsync("someone-else", RequestStatus.Succeeded);
    await AddLines(request.Id, (LogSource.Stdout, "visible to admins"));

    var events = Sse.Parse(await factory.CreateAdminClient().GetStringAsync($"/api/requests/{request.Id}/logs"));

    Assert.Contains(events, e => e.Type == "log");
  }

  [Fact]
  public async Task Logs_FollowsRunningJob_UntilItFinishes()
  {
    var request = await factory.SeedRequestAsync(_userId, RequestStatus.Applying);

    var reading = User.GetStringAsync($"/api/requests/{request.Id}/logs");
    await Task.Delay(100);
    await AddLines(request.Id, (LogSource.Stdout, "late line"));
    await SetStatus(request.Id, RequestStatus.Succeeded);

    // The line and the new status may arrive in the same poll, so only their relative order with the end is fixed.
    var events = Sse.Parse(await reading.WaitAsync(TimeSpan.FromSeconds(10)));
    Assert.Equal(("status", "Applying"), (events[0].Type, events[0].Data["status"]!.GetValue<string>()));
    Assert.Contains(events, e => e.Type == "status" && e.Data["status"]!.GetValue<string>() == "Succeeded");
    Assert.Equal("late line", Assert.Single(events, e => e.Type == "log").Data["text"]!.GetValue<string>());
    Assert.Equal(("end", "Succeeded"), (events[^1].Type, events[^1].Data["status"]!.GetValue<string>()));
  }

  private async Task<List<long>> AddLines(Guid requestId, params (LogSource Source, string Text)[] lines)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var entities = lines
      .Select(l => new JobLogLine
      {
        RequestId = requestId, At = DateTimeOffset.UtcNow, Stream = l.Source, Text = l.Text,
      })
      .ToList();
    db.JobLogLines.AddRange(entities);
    await db.SaveChangesAsync();
    return [.. entities.Select(e => e.Id)];
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
