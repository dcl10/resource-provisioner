namespace ResourceProvisioner.Api.Tests;

using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Config;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Services.Agent;
using Xunit;

/// <summary>
/// The mapping between the agent's wire-format blocks and the Anthropic SDK, checked against the request the SDK
/// would send. No call reaches Anthropic.
/// </summary>
public class AnthropicChatModelTests
{
  private const string StoredHistory = """
    [
      { "role": "user", "content": [ { "type": "text", "text": "a firewall please" } ] },
      { "role": "assistant", "content": [
        { "type": "thinking", "thinking": "", "signature": "sig-1" },
        { "type": "redacted_thinking", "data": "opaque" },
        { "type": "text", "text": "Checking." },
        { "type": "tool_use", "id": "toolu_1", "name": "get_resource_spec", "input": { "type": "firewall" } }
      ] },
      { "role": "user", "content": [
        { "type": "tool_result", "tool_use_id": "toolu_1", "content": "{\"id\":\"firewall\"}", "is_error": false }
      ] }
    ]
    """;

  [Fact]
  public void BuildParams_SendsModelEffortFallbackCacheControlAndBeta()
  {
    var request = Body(Model(effort: "medium").BuildParams(Request([])));

    var body = request["BodyData"]!;
    Assert.Equal("claude-sonnet-5-5", body["model"]!.GetValue<string>());
    Assert.Equal(AnthropicChatModel.MaxTokens, body["max_tokens"]!.GetValue<int>());
    Assert.Equal("default", body["fallbacks"]!.GetValue<string>());
    Assert.Equal("medium", body["output_config"]!["effort"]!.GetValue<string>());
    Assert.Equal("ephemeral", body["cache_control"]!["type"]!.GetValue<string>());
    Assert.Equal(SystemPrompt.Text, body["system"]!.GetValue<string>());
    Assert.Null(body["thinking"]);
    Assert.Equal(
      AnthropicChatModel.FallbackBeta,
      Assert.Single(request["HeaderData"]!["anthropic-beta"]!.AsArray())!.GetValue<string>());
  }

  [Fact]
  public void BuildParams_SendsEveryToolStrict_WithItsSchemaUnchanged()
  {
    var schema = new JsonObject
    {
      ["type"] = "object",
      ["properties"] = new JsonObject
      {
        ["type"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("a") },
      },
      ["required"] = new JsonArray("type"),
      ["additionalProperties"] = false,
    };

    var parameters = Model().BuildParams(Request([], new AgentTool("get_it", "Gets it.", schema)));

    var tools = Body(parameters)["BodyData"]!["tools"]!;

    var tool = Assert.Single(tools.AsArray())!;
    Assert.Equal("get_it", tool["name"]!.GetValue<string>());
    Assert.True(tool["strict"]!.GetValue<bool>());
    Assert.True(JsonNode.DeepEquals(schema, tool["input_schema"]));
  }

  [Fact]
  public void BuildParams_SendsStoredBlocksBackExactly()
  {
    var history = JsonNode.Parse(StoredHistory)!.AsArray()
      .Select(m => new ChatTurn(m!["role"]!.GetValue<string>(), m["content"]!.AsArray()))
      .ToList();

    var messages = Body(Model().BuildParams(Request(history)))["BodyData"]!["messages"];

    Assert.True(JsonNode.DeepEquals(JsonNode.Parse(StoredHistory), messages), messages!.ToJsonString());
  }

  [Fact]
  public void ToBlocks_ConvertsTextThinkingAndToolUse()
  {
    var content = Blocks("""
      [
        { "type": "thinking", "thinking": "", "signature": "sig-1" },
        { "type": "text", "text": "Checking.", "citations": null },
        { "type": "tool_use", "id": "toolu_1", "name": "list_my_resources", "input": { "a": [1, "b"] } }
      ]
      """);

    var blocks = AnthropicChatModel.ToBlocks(content);

    Assert.True(JsonNode.DeepEquals(
      JsonNode.Parse("""
        [
          { "type": "thinking", "thinking": "", "signature": "sig-1" },
          { "type": "text", "text": "Checking." },
          { "type": "tool_use", "id": "toolu_1", "name": "list_my_resources", "input": { "a": [1, "b"] } }
        ]
        """),
      blocks), blocks.ToJsonString());
  }

  [Fact]
  public void ToBlocks_KeepsOnlyTextBeforeTheLastFallback_AndDropsTheMarkers()
  {
    var content = Blocks("""
      [
        { "type": "thinking", "thinking": "", "signature": "declined" },
        { "type": "text", "text": "Partial ", "citations": null },
        { "type": "tool_use", "id": "toolu_0", "name": "list_resource_types", "input": {} },
        { "type": "fallback", "from": { "model": "claude-sonnet-5-5" }, "to": { "model": "claude-sonnet-5" } },
        { "type": "thinking", "thinking": "", "signature": "served" },
        { "type": "text", "text": "answer", "citations": null },
        { "type": "tool_use", "id": "toolu_1", "name": "list_resource_types", "input": {} }
      ]
      """);

    var blocks = AnthropicChatModel.ToBlocks(content);

    Assert.Equal(
      ["text:Partial ", "thinking:served", "text:answer", "tool_use:toolu_1"],
      blocks.Select(b => $"{b!["type"]}:{b["text"] ?? b["signature"] ?? b["id"]}"));
  }

  [Fact]
  public async Task StreamAsync_Throws_WhenNoApiKeyIsConfigured()
  {
    var model = new AnthropicChatModel(
      new AnthropicClient { ApiKey = "" },
      Options.Create(new AnthropicOptions { ApiKey = "" }),
      NullLogger<AnthropicChatModel>.Instance);

    var ex = await Assert.ThrowsAsync<ChatModelException>(async () =>
    {
      await foreach (var _ in model.StreamAsync(Request([]), CancellationToken.None))
      {
      }
    });
    Assert.Contains("Anthropic:ApiKey", ex.Message);
  }

  private static AnthropicChatModel Model(string effort = "low") =>
    new(
      new AnthropicClient { ApiKey = "test-key" },
      Options.Create(new AnthropicOptions { ApiKey = "test-key", Effort = effort }),
      NullLogger<AnthropicChatModel>.Instance);

  private static ChatModelRequest Request(List<ChatTurn> history, params AgentTool[] tools) =>
    new(SystemPrompt.Text, tools, history.Count > 0 ? history : [new ChatTurn("user", [ContentBlocks.Text("hi")])]);

  /// <summary>The request as the SDK would send it: <c>BodyData</c> and <c>HeaderData</c>.</summary>
  private static JsonNode Body(MessageCreateParams parameters) => JsonNode.Parse(parameters.ToString())!;

  private static List<BetaContentBlock> Blocks(string json) =>
    [.. JsonNode.Parse(json)!.AsArray().Select(b => JsonSerializer.Deserialize<BetaContentBlock>(b!.ToJsonString())!)];
}
