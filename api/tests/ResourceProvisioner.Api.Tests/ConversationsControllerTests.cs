namespace ResourceProvisioner.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Models;
using Services.Agent;
using Xunit;

/// <summary>A test host that lets each user send only two chat messages a minute.</summary>
public class RateLimitApiFactory : ApiFactory
{
  /// <inheritdoc />
  protected override void ConfigureWebHost(IWebHostBuilder builder)
  {
    base.ConfigureWebHost(builder);
    builder.ConfigureAppConfiguration(config =>
      config.AddInMemoryCollection(new Dictionary<string, string?> { ["Limits:ChatMessagesPerMinute"] = "2" }));
  }
}

/// <summary>Creating, listing and reading conversations, and the chat rate limit.</summary>
public class ConversationsControllerTests : IClassFixture<ApiFactory>, IClassFixture<RateLimitApiFactory>
{
  private const string Route = "/api/conversations";

  private readonly ApiFactory _factory;
  private readonly RateLimitApiFactory _limited;
  private readonly string _userId = $"user-{Guid.NewGuid():N}";

  public ConversationsControllerTests(ApiFactory factory, RateLimitApiFactory limited)
  {
    _factory = factory;
    _limited = limited;
    _factory.ChatModel.Reset();
  }

  private HttpClient User => _factory.CreateUserClient(_userId, $"{_userId}@example.com");

  [Theory]
  [InlineData("GET", Route)]
  [InlineData("POST", Route)]
  [InlineData("GET", $"{Route}/00000000-0000-0000-0000-000000000001")]
  [InlineData("POST", $"{Route}/00000000-0000-0000-0000-000000000001/messages")]
  public async Task Endpoints_ReturnUnauthorized_WithoutCredentials(string method, string url)
  {
    var request = new HttpRequestMessage(new HttpMethod(method), url);

    var response = await _factory.CreateAnonymousClient().SendAsync(request);

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task Create_ReturnsCreatedEmptyConversation_OwnedByTheUser()
  {
    var response = await User.PostAsync(Route, null);

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var conversation = await response.Content.ReadFromJsonAsync<ConversationSummaryResponse>();
    Assert.Equal(_userId, conversation!.OwnerId);
    Assert.Null(conversation.Title);
    Assert.EndsWith($"{Route}/{conversation.Id}", response.Headers.Location!.ToString());
  }

  [Fact]
  public async Task List_ReturnsOnlyOwnConversations_MostRecentlyActiveFirst()
  {
    var older = await Create(User);
    var newer = await Create(User);
    var others = await Create(_factory.CreateUserClient("someone-else"));
    await User.PostAsJsonAsync($"{Route}/{older}/messages", new { text = "bump" });

    var list = await User.GetFromJsonAsync<List<ConversationSummaryResponse>>(Route);

    Assert.NotNull(list);
    Assert.Equal([older, newer], list.Select(c => c.Id));
    Assert.DoesNotContain(list, c => c.Id == others);
  }

  [Fact]
  public async Task List_ReturnsEveryonesConversations_ForAdmin()
  {
    var mine = await Create(User);

    var list = await _factory.CreateAdminClient().GetFromJsonAsync<List<ConversationSummaryResponse>>(Route);

    Assert.Contains(list!, c => c.Id == mine && c.OwnerId == _userId);
  }

  [Fact]
  public async Task Get_ReturnsTheChatAsShown_WithProposalsAndWithoutToolInternals()
  {
    var id = await Create(User);
    var project = await _factory.DefaultProjectAsync(_userId);
    _factory.ChatModel.ReplyToolUse(
      AgentTools.ProposeRequest,
      $$$"""{"type":"test-server","project_id":"{{{project.Id}}}","variables":{"name":"web-1"}}""",
      text: "Proposing it now.");
    _factory.ChatModel.ReplyText("Review the card and click Confirm.");
    await User.PostAsJsonAsync($"{Route}/{id}/messages", new { text = "a server called web-1" });

    var conversation = await User.GetFromJsonAsync<ConversationResponse>($"{Route}/{id}");

    Assert.Equal(["user", "assistant"], conversation!.Messages.Select(m => m.Role));
    Assert.Equal("a server called web-1", conversation.Messages[0].Text);
    var reply = conversation.Messages[1];
    Assert.Equal("Proposing it now.\n\nReview the card and click Confirm.", reply.Text);
    var proposal = Assert.Single(reply.Proposals);
    Assert.Equal("test-server", proposal.Type);
    Assert.Contains(proposal.Items, i => i is { Name: "name", Value: "web-1" });
    Assert.Equal(new ProjectLink(project.Id, project.Name), proposal.Project);
  }

  [Fact]
  public async Task Get_ReturnsNotFound_ForAnotherUsersConversation()
  {
    var others = await Create(_factory.CreateUserClient("someone-else"));

    var response = await User.GetAsync($"{Route}/{others}");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Get_ReturnsAnyConversation_ForAdmin()
  {
    var mine = await Create(User);

    var response = await _factory.CreateAdminClient().GetAsync($"{Route}/{mine}");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task SendMessage_ReturnsTooManyRequests_OverTheUsersLimit()
  {
    var user = _limited.CreateUserClient(_userId);
    var id = await Create(user);

    for (var i = 0; i < 2; i++)
    {
      var ok = await user.PostAsJsonAsync($"{Route}/{id}/messages", new { text = $"message {i}" });
      Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    var limited = await user.PostAsJsonAsync($"{Route}/{id}/messages", new { text = "one too many" });
    var otherUser = _limited.CreateUserClient($"other-{Guid.NewGuid():N}");
    var otherConversation = await Create(otherUser);
    var unaffected = await otherUser.PostAsJsonAsync($"{Route}/{otherConversation}/messages", new { text = "hi" });

    Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
    Assert.NotNull(limited.Headers.RetryAfter);
    Assert.Equal(HttpStatusCode.OK, unaffected.StatusCode);
  }

  private static async Task<Guid> Create(HttpClient client) =>
    (await (await client.PostAsync(Route, null)).Content.ReadFromJsonAsync<ConversationSummaryResponse>())!.Id;
}
