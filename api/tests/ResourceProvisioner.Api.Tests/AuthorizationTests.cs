namespace ResourceProvisioner.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using Endpoints;
using Fixtures;
using Xunit;

public class AuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  [Fact]
  public async Task GetMe_ReturnsUnauthorized_WithoutCredentials()
  {
    var response = await factory.CreateAnonymousClient().GetAsync("/api/me");

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task GetMe_ReturnsCurrentUser_WhenSignedIn()
  {
    var response = await factory.CreateUserClient("user-42", "someone@example.com").GetAsync("/api/me");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var me = await response.Content.ReadFromJsonAsync<MeEndpoints.MeResponse>();
    Assert.Equal(new MeEndpoints.MeResponse("user-42", "someone@example.com", false), me);
  }

  [Fact]
  public async Task GetMe_ReportsAdmin_WhenUserHasAdminRealmRole()
  {
    var me = await factory.CreateAdminClient().GetFromJsonAsync<MeEndpoints.MeResponse>("/api/me");

    Assert.True(me!.IsAdmin);
  }

  [Fact]
  public async Task GetMe_ReturnsNullEmail_WhenTokenHasNoEmail()
  {
    var me = await factory.CreateUserClient(email: null).GetFromJsonAsync<MeEndpoints.MeResponse>("/api/me");

    Assert.Null(me!.Email);
  }

  [Fact]
  public async Task AdminOnlyEndpoint_ReturnsUnauthorized_WithoutCredentials()
  {
    var response = await factory.CreateAnonymousClient().GetAsync(TestEndpoints.AdminOnly);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task AdminOnlyEndpoint_ReturnsForbidden_ForNonAdmin()
  {
    var response = await factory.CreateUserClient().GetAsync(TestEndpoints.AdminOnly);

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task AdminOnlyEndpoint_ReturnsOk_ForAdmin()
  {
    var response = await factory.CreateAdminClient().GetAsync(TestEndpoints.AdminOnly);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task GetHealth_StaysAnonymous_WithFallbackPolicy()
  {
    var response = await factory.CreateAnonymousClient().GetAsync("/health");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }
}
