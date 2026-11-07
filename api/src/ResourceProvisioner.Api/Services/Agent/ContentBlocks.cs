namespace ResourceProvisioner.Api.Services.Agent;

using System.Text.Json.Nodes;

/// <summary>Builds and reads content blocks in the Messages API's wire format.</summary>
public static class ContentBlocks
{
  /// <summary>A <c>text</c> block.</summary>
  /// <param name="text">The text.</param>
  /// <returns>The block.</returns>
  public static JsonObject Text(string text) => new() { ["type"] = "text", ["text"] = text };

  /// <summary>A <c>tool_use</c> block.</summary>
  /// <param name="id">The call's ID.</param>
  /// <param name="name">The tool.</param>
  /// <param name="input">Its input.</param>
  /// <returns>The block.</returns>
  public static JsonObject ToolUse(string id, string name, JsonObject input) =>
    new() { ["type"] = "tool_use", ["id"] = id, ["name"] = name, ["input"] = input };

  /// <summary>A <c>tool_result</c> block.</summary>
  /// <param name="toolUseId">The call it answers.</param>
  /// <param name="content">The result, usually JSON text.</param>
  /// <param name="isError">Whether the call failed.</param>
  /// <returns>The block.</returns>
  public static JsonObject ToolResult(string toolUseId, string content, bool isError) =>
    new() { ["type"] = "tool_result", ["tool_use_id"] = toolUseId, ["content"] = content, ["is_error"] = isError };

  /// <summary>The <c>type</c> of a block.</summary>
  /// <param name="block">The block.</param>
  /// <returns>Its type, or an empty string.</returns>
  public static string TypeOf(JsonNode? block) => block?["type"]?.GetValue<string>() ?? "";

  /// <summary>The blocks of one type.</summary>
  /// <param name="content">A message's content.</param>
  /// <param name="type">The block type, such as <c>tool_use</c>.</param>
  /// <returns>The matching blocks.</returns>
  public static IEnumerable<JsonObject> OfType(JsonArray content, string type) =>
    content.OfType<JsonObject>().Where(b => TypeOf(b) == type);

  /// <summary>The text of a message's <c>text</c> blocks, joined.</summary>
  /// <param name="content">A message's content.</param>
  /// <returns>The text; empty if there is none.</returns>
  public static string JoinedText(JsonArray content) =>
    string.Concat(OfType(content, "text").Select(b => b["text"]?.GetValue<string>() ?? ""));
}
