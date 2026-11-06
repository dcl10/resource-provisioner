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

public class InvitationsControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private const string Route = "/api/invitations";

  private readonly HttpClient _admin = factory.CreateAdminClient();

  [Theory]
  [InlineData("GET", Route)]
  [InlineData("POST", Route)]
  [InlineData("POST", $"{Route}/00000000-0000-0000-0000-000000000001/resend")]
  [InlineData("POST", $"{Route}/00000000-0000-0000-0000-000000000001/revoke")]
  public async Task Endpoints_ReturnForbidden_ForNonAdmin(string method, string url)
  {
    var response = await factory.CreateUserClient().SendAsync(Request(method, url));

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Theory]
  [InlineData("GET", Route)]
  [InlineData("POST", Route)]
  public async Task Endpoints_ReturnUnauthorized_WithoutCredentials(string method, string url)
  {
    var response = await factory.CreateAnonymousClient().SendAsync(Request(method, url));

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Invite_CreatesUserAndSendsActionEmail_WhenEmailIsNew()
  {
    var response = await _admin.PostAsJsonAsync(Route, new { email = "  New.Person@Example.com " });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var invitation = await response.Content.ReadFromJsonAsync<InvitationResponse>();
    Assert.Equal("new.person@example.com", invitation!.Email);
    Assert.Equal(InvitationStatus.Pending, invitation.Status);
    Assert.Equal("admin-1", invitation.InvitedBy);
    Assert.Equal(TimeSpan.FromDays(7), invitation.ExpiresAt - invitation.SentAt);
    Assert.Equal($"api/invitations/{invitation.Id}", response.Headers.Location?.ToString());

    var stored = await FindStored(invitation.Id);
    var user = factory.Keycloak.Find(stored.KeycloakUserId);
    Assert.NotNull(user);
    Assert.Equal("new.person@example.com", user.Email);
    Assert.False(user.EmailVerified);
    Assert.True(user.Enabled);

    var email = Assert.Single(factory.Keycloak.Emails, x => x.UserId == stored.KeycloakUserId);
    Assert.Equal(["VERIFY_EMAIL", "UPDATE_PASSWORD"], email.Actions);
    Assert.Equal(TimeSpan.FromDays(7), email.Lifespan);
    Assert.Equal("rp-web", email.ClientId);
    Assert.Equal("http://localhost:3000", email.RedirectUri);
  }

  [Fact]
  public async Task Invite_ReturnsConflict_WhenEmailAlreadyHasAccount()
  {
    factory.Keycloak.AddUser("existing@example.com");

    var response = await _admin.PostAsJsonAsync(Route, new { email = "Existing@example.com" });

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    Assert.Equal("existing@example.com already has an account.", problem!.Detail);
  }

  [Theory]
  [InlineData("")]
  [InlineData("not-an-email")]
  public async Task Invite_ReturnsBadRequest_WhenEmailIsInvalid(string email)
  {
    var response = await _admin.PostAsJsonAsync(Route, new { email });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Invite_DeletesUserAndReturnsBadGateway_WhenEmailFails()
  {
    // Its own host, so failing emails don't affect the other tests.
    await using var failing = new ApiFactory();
    failing.Keycloak.FailEmails = true;

    var response = await failing.CreateAdminClient().PostAsJsonAsync(Route, new { email = "unlucky@example.com" });

    Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    Assert.Null(await failing.Keycloak.FindByEmailAsync("unlucky@example.com"));
    var list = await failing.CreateAdminClient().GetFromJsonAsync<List<InvitationResponse>>(Route);
    Assert.Empty(list!);
  }

  [Fact]
  public async Task List_ReportsAccepted_WhenKeycloakEmailIsVerified()
  {
    var invitation = await Invite("accepts@example.com");
    factory.Keycloak.VerifyEmail((await FindStored(invitation.Id)).KeycloakUserId);

    var listed = await FindListed(invitation.Id);

    Assert.Equal(InvitationStatus.Accepted, listed.Status);
  }

  [Fact]
  public async Task List_ReportsExpired_WhenPastExpiryAndNotAccepted()
  {
    var invitation = await Invite("expires@example.com");
    await Expire(invitation.Id);

    var listed = await FindListed(invitation.Id);

    Assert.Equal(InvitationStatus.Expired, listed.Status);
  }

  [Fact]
  public async Task Resend_SendsFreshEmailAndExpiry_WhenExpired()
  {
    var invitation = await Invite("resend@example.com");
    await Expire(invitation.Id);
    var userId = (await FindStored(invitation.Id)).KeycloakUserId;

    var response = await _admin.PostAsync($"{Route}/{invitation.Id}/resend", null);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var resent = await response.Content.ReadFromJsonAsync<InvitationResponse>();
    Assert.Equal(InvitationStatus.Pending, resent!.Status);
    Assert.True(resent.ExpiresAt > DateTimeOffset.UtcNow.AddDays(6));
    Assert.Equal(2, factory.Keycloak.Emails.Count(x => x.UserId == userId));
  }

  [Fact]
  public async Task Resend_ReturnsConflict_WhenAccepted()
  {
    var invitation = await Invite("accepted-resend@example.com");
    factory.Keycloak.VerifyEmail((await FindStored(invitation.Id)).KeycloakUserId);

    var response = await _admin.PostAsync($"{Route}/{invitation.Id}/resend", null);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  [Fact]
  public async Task Resend_ReturnsNotFound_WhenInvitationDoesNotExist()
  {
    var response = await _admin.PostAsync($"{Route}/{Guid.NewGuid()}/resend", null);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Revoke_DisablesKeycloakUser_WhenPending()
  {
    var invitation = await Invite("revoke@example.com");
    var userId = (await FindStored(invitation.Id)).KeycloakUserId;

    var response = await _admin.PostAsync($"{Route}/{invitation.Id}/revoke", null);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var revoked = await response.Content.ReadFromJsonAsync<InvitationResponse>();
    Assert.Equal(InvitationStatus.Revoked, revoked!.Status);
    Assert.NotNull(revoked.RevokedAt);
    Assert.False(factory.Keycloak.Find(userId)!.Enabled);
    Assert.Equal(InvitationStatus.Revoked, (await FindListed(invitation.Id)).Status);
  }

  [Fact]
  public async Task Revoke_ReturnsConflict_WhenAccepted()
  {
    var invitation = await Invite("accepted-revoke@example.com");
    var userId = (await FindStored(invitation.Id)).KeycloakUserId;
    factory.Keycloak.VerifyEmail(userId);

    var response = await _admin.PostAsync($"{Route}/{invitation.Id}/revoke", null);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    Assert.True(factory.Keycloak.Find(userId)!.Enabled);
  }

  [Fact]
  public async Task Resend_ReturnsConflict_WhenRevoked()
  {
    var invitation = await Invite("revoked-resend@example.com");
    await _admin.PostAsync($"{Route}/{invitation.Id}/revoke", null);

    var response = await _admin.PostAsync($"{Route}/{invitation.Id}/resend", null);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  private static HttpRequestMessage Request(string method, string url) =>
    new(new HttpMethod(method), url)
    {
      Content = method == "POST" ? JsonContent.Create(new { email = "x@example.com" }) : null,
    };

  private async Task<InvitationResponse> Invite(string email)
  {
    var response = await _admin.PostAsJsonAsync(Route, new { email });
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<InvitationResponse>())!;
  }

  private async Task<InvitationResponse> FindListed(Guid id)
  {
    var list = await _admin.GetFromJsonAsync<List<InvitationResponse>>(Route);
    return Assert.Single(list!, x => x.Id == id);
  }

  private async Task<Invitation> FindStored(Guid id)
  {
    using var scope = factory.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Invitations.SingleAsync(x => x.Id == id);
  }

  private async Task Expire(Guid id)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var invitation = await db.Invitations.SingleAsync(x => x.Id == id);
    invitation.ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1);
    await db.SaveChangesAsync();
  }
}
