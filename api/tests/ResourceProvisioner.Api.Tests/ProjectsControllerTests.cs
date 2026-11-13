namespace ResourceProvisioner.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using Data;
using Fixtures;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Xunit;

/// <summary><c>/api/projects</c>: listing, reading and creating projects, and who can see them.</summary>
public class ProjectsControllerTests(HighLimitApiFactory factory) : IClassFixture<HighLimitApiFactory>
{
  private const string Route = "/api/projects";

  private readonly string _userId = $"user-{Guid.NewGuid():N}";

  private HttpClient User => factory.CreateUserClient(_userId, $"{_userId}@example.com");

  [Theory]
  [InlineData("GET", Route)]
  [InlineData("GET", $"{Route}/00000000-0000-0000-0000-000000000001")]
  [InlineData("POST", Route)]
  public async Task Endpoints_ReturnUnauthorized_WithoutCredentials(string method, string url)
  {
    var response = await factory.CreateAnonymousClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), url));

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Create_ProposesAndConfirmsAProjectRequest_AndReturnsTheProject()
  {
    var response = await User.PostAsJsonAsync(
      Route,
      new { variables = new { name = "shop", environment = "Staging" } });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var project = (await response.Content.ReadFromJsonAsync<ProjectDetailResponse>())!;
    Assert.Equal($"/api/projects/{project.Id}", response.Headers.Location!.AbsolutePath);
    Assert.Equal(
      ("shop", "Staging", "Web Application", null, _userId, RequestStatus.Queued, 0),
      (project.Name, project.Environment, project.Purpose, project.Description, project.OwnerId, project.Status,
        project.ResourceCount));
    Assert.Equal(Project.RequestType, project.Request.Type);
    Assert.Equal(project.ProjectRequestId, project.Request.Id);
    Assert.Equal(new ProjectLink(project.Id, "shop"), project.Request.Project);
    Assert.Empty(project.Resources);

    var request = await factory.FindRequestAsync(project.ProjectRequestId);
    Assert.Equal(RequestStatus.Queued, request!.Status);
    Assert.NotNull(request.ConfirmedAt);
    Assert.Null(request.ProjectId);
    Assert.Null(request.ConversationId);
  }

  [Fact]
  public async Task Create_Returns422AndKeepsNothing_WhenTheVariablesAreInvalid()
  {
    var response = await User.PostAsJsonAsync(Route, new { variables = new { name = "Shop", purpose = "Mining" } });

    Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
    Assert.Contains("name", problem!.Errors.Keys);
    Assert.Contains("purpose", problem.Errors.Keys);
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    Assert.False(await db.ProvisioningRequests.AnyAsync(r => r.OwnerId == _userId));
    Assert.False(await db.Projects.AnyAsync(p => p.OwnerId == _userId));
  }

  [Fact]
  public async Task Create_Returns400_WithoutVariables()
  {
    var response = await User.PostAsJsonAsync(Route, new { });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Create_Succeeds_WhenTheUserIsAtTheirLimit_BecauseProjectsAreFree()
  {
    for (var i = 0; i < 3; i++)
    {
      await factory.SeedRequestAsync(_userId, RequestStatus.Succeeded);
    }

    var response = await User.PostAsJsonAsync(Route, new { variables = new { name = "shop" } });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
  }

  [Fact]
  public async Task List_ReturnsOwnProjects_WithStatusAndResourceCount_NewestFirst()
  {
    var older = await factory.SeedProjectAsync(_userId, name: "older");
    var newer = await factory.SeedProjectAsync(_userId, RequestStatus.AwaitingConfirmation, name: "newer");
    await factory.SeedRequestAsync(_userId, RequestStatus.Succeeded, projectId: older.Id);
    await factory.SeedRequestAsync(_userId, RequestStatus.Queued, projectId: older.Id);
    await factory.SeedRequestAsync(_userId, RequestStatus.Destroyed, projectId: older.Id);
    var others = await factory.SeedProjectAsync("someone-else");

    var list = (await User.GetFromJsonAsync<List<ProjectResponse>>(Route))!;

    Assert.Equal([newer.Id, older.Id], list.Select(p => p.Id));
    Assert.Equal((RequestStatus.Succeeded, 2), (list[1].Status, list[1].ResourceCount));
    Assert.Equal((RequestStatus.AwaitingConfirmation, 0), (list[0].Status, list[0].ResourceCount));
    Assert.DoesNotContain(list, p => p.Id == others.Id);
  }

  [Theory]
  [InlineData(RequestStatus.Cancelled)]
  [InlineData(RequestStatus.Destroyed)]
  public async Task List_LeavesOutAProject_WhoseRequestIsGone(RequestStatus status)
  {
    var gone = await factory.SeedProjectAsync(_userId, status);

    var list = await User.GetFromJsonAsync<List<ProjectResponse>>(Route);

    Assert.DoesNotContain(list!, p => p.Id == gone.Id);
  }

  [Fact]
  public async Task List_ReturnsEveryUsersProjects_WithOwner_ForAdmin()
  {
    var others = await factory.SeedProjectAsync("someone-else");

    var list = await factory.CreateAdminClient().GetFromJsonAsync<List<ProjectResponse>>(Route);

    Assert.Contains(list!, p => p.Id == others.Id && p.OwnerId == "someone-else" &&
                                p.OwnerEmail == "someone-else@example.com");
  }

  [Fact]
  public async Task Get_ReturnsTheProject_WithItsRequestAndResources()
  {
    var project = await factory.SeedProjectAsync(_userId, name: "shop");
    var server = await factory.SeedRequestAsync(_userId, RequestStatus.Succeeded, projectId: project.Id);
    await factory.AddDependencyAsync(server.Id, project.ProjectRequestId);
    var elsewhere = await factory.SeedRequestAsync(_userId, RequestStatus.Succeeded);

    var body = await User.GetFromJsonAsync<ProjectDetailResponse>($"{Route}/{project.Id}");

    Assert.Equal(("shop", RequestStatus.Succeeded, 1), (body!.Name, body.Status, body.ResourceCount));
    Assert.Equal(project.ProjectRequestId, body.Request.Id);
    var resource = Assert.Single(body.Resources);
    Assert.Equal(server.Id, resource.Id);
    Assert.Equal(new ProjectLink(project.Id, "shop"), resource.Project);
    Assert.Equal(project.ProjectRequestId, Assert.Single(resource.DependsOn).Id);
    Assert.Equal(server.Id, Assert.Single(body.Request.Dependents).Id);
    Assert.DoesNotContain(body.Resources, r => r.Id == elsewhere.Id);
  }

  [Fact]
  public async Task Get_ReturnsNotFound_ForAnotherUsersProject()
  {
    var others = await factory.SeedProjectAsync("someone-else");

    var response = await User.GetAsync($"{Route}/{others.Id}");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Get_ReturnsAnotherUsersProject_ForAdmin()
  {
    var others = await factory.SeedProjectAsync("someone-else");
    var server = await factory.SeedRequestAsync("someone-else", RequestStatus.Succeeded, projectId: others.Id);

    var body = await factory.CreateAdminClient().GetFromJsonAsync<ProjectDetailResponse>($"{Route}/{others.Id}");

    Assert.Equal("someone-else", body!.OwnerId);
    Assert.Equal(server.Id, Assert.Single(body.Resources).Id);
  }

  [Theory]
  [InlineData(RequestStatus.Cancelled)]
  [InlineData(RequestStatus.Destroyed)]
  public async Task Get_ReturnsNotFound_WhenTheProjectsRequestIsGone(RequestStatus status)
  {
    var gone = await factory.SeedProjectAsync(_userId, status);

    var response = await User.GetAsync($"{Route}/{gone.Id}");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Get_ReturnsNotFound_ForAnUnknownProject()
  {
    var response = await User.GetAsync($"{Route}/{Guid.NewGuid()}");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }
}
