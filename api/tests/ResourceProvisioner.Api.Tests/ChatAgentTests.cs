namespace ResourceProvisioner.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Data;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Services.Agent;
using Xunit;

/// <summary><c>POST /api/conversations/{id}/messages</c>: the agent loop against a scripted Claude.</summary>
public class ChatAgentTests : IClassFixture<ApiFactory>
{
  private readonly ApiFactory _factory;
  private readonly string _userId = $"user-{Guid.NewGuid():N}";

  public ChatAgentTests(ApiFactory factory)
  {
    _factory = factory;
    _factory.ChatModel.Reset();
  }

  private HttpClient User => _factory.CreateUserClient(_userId, $"{_userId}@example.com");

  [Fact]
  public async Task Send_StreamsTextThenDone_AndSavesBothTurns()
  {
    var id = await NewConversation();
    _factory.ChatModel.ReplyText("Which region?");

    var events = await Send(id, "I need a server");

    Assert.Equal(["text", "text", "done"], events.Select(e => e.Type));
    var text = events.Where(e => e.Type == "text").Select(e => e.Data["text"]!.GetValue<string>());
    Assert.Equal("Which region?", string.Concat(text));
    var stored = await Stored(id);
    Assert.Equal(["user", "assistant"], stored.Select(m => m.Role));
    Assert.Equal("I need a server", ContentBlocks.JoinedText(JsonNode.Parse(stored[0].Content)!.AsArray()));
  }

  [Fact]
  public async Task Send_SendsSystemPromptAndFourStrictTools()
  {
    var id = await NewConversation();

    await Send(id, "hi");

    var request = Assert.Single(_factory.ChatModel.Requests);
    Assert.Equal(SystemPrompt.Text, request.System);
    Assert.Equal(
      [AgentTools.ListResourceTypes, AgentTools.GetResourceSpec, AgentTools.ListMyResources, AgentTools.ProposeRequest],
      request.Tools.Select(t => t.Name));
  }

  [Fact]
  public async Task Send_ReplaysEarlierTurns_OnTheNextMessage()
  {
    var id = await NewConversation();
    _factory.ChatModel.ReplyText("First reply.");
    await Send(id, "first");

    await Send(id, "second");

    var second = _factory.ChatModel.Requests.Last();
    Assert.Equal(["user", "assistant", "user"], second.Messages.Select(m => m.Role));
    Assert.Equal(
      ["first", "First reply.", "second"],
      second.Messages.Select(m => ContentBlocks.JoinedText(m.Content)));
  }

  [Fact]
  public async Task Send_RunsToolsAndLoops_UntilClaudeEndsItsTurn()
  {
    var id = await NewConversation();
    _factory.ChatModel.ReplyToolUse(AgentTools.ListResourceTypes, text: "Let me check.");
    _factory.ChatModel.ReplyText("We have a test server.");

    var events = await Send(id, "what can I make?");

    Assert.Equal("done", events[^1].Type);
    var requests = _factory.ChatModel.Requests.ToList();
    Assert.Equal(2, requests.Count);
    var toolResult = ContentBlocks.OfType(requests[1].Messages[^1].Content, "tool_result").Single();
    Assert.Equal("toolu_1", toolResult["tool_use_id"]!.GetValue<string>());
    Assert.False(toolResult["is_error"]!.GetValue<bool>());
    Assert.Contains("test-server", toolResult["content"]!.GetValue<string>());
    Assert.Equal(["user", "assistant", "user", "assistant"], (await Stored(id)).Select(m => m.Role));
  }

  [Fact]
  public async Task Send_ProposesRequest_AndEmitsProposal()
  {
    var id = await NewConversation();
    _factory.ChatModel.ReplyToolUse(
      AgentTools.ProposeRequest,
      """{"type":"test-server","variables":{"name":"web-1","size":"medium"}}""");
    _factory.ChatModel.ReplyText("Review the card and click Confirm.");

    var events = await Send(id, "a medium server called web-1");

    var proposal = Assert.Single(events, e => e.Type == "proposal").Data;
    var requestId = Guid.Parse(proposal["requestId"]!.GetValue<string>());
    Assert.Equal("Test server", proposal["displayName"]!.GetValue<string>());
    var items = proposal["items"]!.AsArray();
    Assert.Equal("medium", items.Single(i => i!["name"]!.GetValue<string>() == "size")!["value"]!.GetValue<string>());
    Assert.Equal("Size", items.Single(i => i!["name"]!.GetValue<string>() == "size")!["label"]!.GetValue<string>());

    var request = await _factory.FindRequestAsync(requestId);
    Assert.Equal(RequestStatus.AwaitingConfirmation, request!.Status);
    Assert.Equal(_userId, request.OwnerId);
    Assert.Equal(id, request.ConversationId);
  }

  [Fact]
  public async Task Send_ReturnsValidationErrorsToClaude_WhenProposalIsInvalid()
  {
    var id = await NewConversation();
    _factory.ChatModel.ReplyToolUse(
      AgentTools.ProposeRequest,
      """{"type":"test-server","variables":{"size":"huge"}}""");
    _factory.ChatModel.ReplyText("What should it be called?");

    var events = await Send(id, "a huge server");

    Assert.DoesNotContain(events, e => e.Type == "proposal");
    var toolResult = LastToolResult();
    Assert.True(toolResult["is_error"]!.GetValue<bool>());
    var errors = JsonNode.Parse(toolResult["content"]!.GetValue<string>())!["errors"]!;
    Assert.NotNull(errors["name"]);
    Assert.NotNull(errors["size"]);
  }

  [Fact]
  public async Task Send_LeavesSensitiveVariablesOutOfTheSpec()
  {
    var id = await NewConversation();
    _factory.ChatModel.ReplyToolUse(AgentTools.GetResourceSpec, """{"type":"test-server"}""");
    _factory.ChatModel.ReplyText("ok");

    await Send(id, "tell me about servers");

    var json = LastToolResult()["content"]!.GetValue<string>();
    Assert.Contains("node_count", json);
    Assert.DoesNotContain("admin_password", json);
  }

  [Fact]
  public async Task Send_ListsOnlyTheUsersOwnResources_WithoutSecrets()
  {
    var id = await NewConversation();
    var mine = await _factory.SeedRequestAsync(
      _userId,
      RequestStatus.Succeeded,
      outputs: """{"ipv4_address":"192.0.2.1"}""",
      sensitiveOutputs: """{"root_password":"output-secret"}""");
    var destroyed = await _factory.SeedRequestAsync(_userId, RequestStatus.Destroyed);
    var others = await _factory.SeedRequestAsync("someone-else", RequestStatus.Succeeded);
    _factory.ChatModel.ReplyToolUse(AgentTools.ListMyResources);
    _factory.ChatModel.ReplyText("ok");

    await Send(id, "what do I have?");

    var json = LastToolResult()["content"]!.GetValue<string>();
    Assert.Contains(mine.Id.ToString(), json);
    Assert.Contains("192.0.2.1", json);
    Assert.DoesNotContain("output-secret", json);
    Assert.DoesNotContain(destroyed.Id.ToString(), json);
    Assert.DoesNotContain(others.Id.ToString(), json);
  }

  [Fact]
  public async Task Send_ListsOnlyTheAdminsOwnResources_ForAdmin()
  {
    var admin = _factory.CreateAdminClient();
    var id = (await (await admin.PostAsync("/api/conversations", null))
      .Content.ReadFromJsonAsync<ConversationSummaryResponse>())!.Id;
    var others = await _factory.SeedRequestAsync("someone-else", RequestStatus.Succeeded);
    _factory.ChatModel.ReplyToolUse(AgentTools.ListMyResources);
    _factory.ChatModel.ReplyText("ok");

    await admin.PostAsJsonAsync($"/api/conversations/{id}/messages", new { text = "mine?" });

    var json = LastToolResult()["content"]!.GetValue<string>();
    Assert.DoesNotContain(others.Id.ToString(), json);
  }

  [Theory]
  [InlineData("refusal", "declined")]
  [InlineData("max_tokens", "cut off")]
  public async Task Send_ReportsErrorWithoutSavingTheTurn_WhenClaudeStopsEarly(string stopReason, string message)
  {
    var id = await NewConversation();
    _factory.ChatModel.Reply(
      new ChatModelEvent.TextDelta("Partial"),
      new ChatModelEvent.Completed(
        new JsonArray(
          ContentBlocks.Text("Partial"),
          ContentBlocks.ToolUse("toolu_x", AgentTools.ListResourceTypes, [])),
        stopReason));

    var events = await Send(id, "hello");

    Assert.Equal("error", events[^1].Type);
    Assert.Contains(message, events[^1].Data["error"]!.GetValue<string>());
    Assert.Equal(["user"], (await Stored(id)).Select(m => m.Role));
  }

  [Fact]
  public async Task Send_ReportsModelErrors()
  {
    var id = await NewConversation();
    _factory.ChatModel.Fail("Claude is busy right now.");

    var events = await Send(id, "hello");

    Assert.Equal("error", events[^1].Type);
    Assert.Equal("Claude is busy right now.", events[^1].Data["error"]!.GetValue<string>());
  }

  [Fact]
  public async Task Send_StopsAfterMaxModelCalls()
  {
    var id = await NewConversation();
    for (var i = 0; i < ChatAgent.MaxModelCalls; i++)
    {
      _factory.ChatModel.ReplyToolUse(AgentTools.ListResourceTypes, id: $"toolu_{i}");
    }

    var events = await Send(id, "loop forever");

    Assert.Equal(ChatAgent.MaxModelCalls, _factory.ChatModel.Requests.Count);
    Assert.Equal("error", events[^1].Type);
    Assert.Contains("too many steps", events[^1].Data["error"]!.GetValue<string>());
  }

  [Fact]
  public async Task Send_SetsTitleFromTheFirstMessage()
  {
    var id = await NewConversation();

    await Send(id, "  A Postgres database for the billing service  ");
    await Send(id, "something else");

    var conversation = await User.GetFromJsonAsync<ConversationResponse>($"/api/conversations/{id}");
    Assert.Equal("A Postgres database for the billing service", conversation!.Title);
  }

  [Fact]
  public async Task Send_ReturnsNotFound_ForAnotherUsersConversation()
  {
    var id = await NewConversation();

    var response = await _factory.CreateUserClient("someone-else")
      .PostAsJsonAsync($"/api/conversations/{id}/messages", new { text = "hi" });

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    Assert.Empty(_factory.ChatModel.Requests);
  }

  [Fact]
  public async Task Send_ReturnsForbidden_ForAdminInAnotherUsersConversation()
  {
    var id = await NewConversation();

    var response = await _factory.CreateAdminClient()
      .PostAsJsonAsync($"/api/conversations/{id}/messages", new { text = "hi" });

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    Assert.Empty(_factory.ChatModel.Requests);
  }

  [Theory]
  [InlineData("""{"text":""}""")]
  [InlineData("""{}""")]
  public async Task Send_ReturnsBadRequest_WhenTextIsMissing(string body)
  {
    var id = await NewConversation();

    var response = await User.PostAsync(
      $"/api/conversations/{id}/messages",
      new StringContent(body, System.Text.Encoding.UTF8, "application/json"));

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Send_ReturnsBadRequest_WhenTextIsTooLong()
  {
    var id = await NewConversation();

    var response = await User.PostAsJsonAsync(
      $"/api/conversations/{id}/messages",
      new { text = new string('x', SendMessageRequest.MaxLength + 1) });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  private async Task<Guid> NewConversation()
  {
    var response = await User.PostAsync("/api/conversations", null);
    return (await response.Content.ReadFromJsonAsync<ConversationSummaryResponse>())!.Id;
  }

  private async Task<List<SseEvent>> Send(Guid id, string text)
  {
    var response = await User.PostAsJsonAsync($"/api/conversations/{id}/messages", new { text });
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    return Sse.Parse(await response.Content.ReadAsStringAsync());
  }

  /// <summary>The single tool result in the last request sent to Claude.</summary>
  private JsonObject LastToolResult() =>
    ContentBlocks.OfType(_factory.ChatModel.Requests.Last().Messages[^1].Content, "tool_result").Single();

  private async Task<List<ConversationMessage>> Stored(Guid id)
  {
    using var scope = _factory.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ConversationMessages
      .Where(x => x.ConversationId == id)
      .OrderBy(x => x.Id)
      .ToListAsync();
  }
}
