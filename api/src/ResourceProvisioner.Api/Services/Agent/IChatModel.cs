namespace ResourceProvisioner.Api.Services.Agent;

using System.Text.Json.Nodes;

/// <summary>
/// Claude, as the agent sees it: one streamed Messages API call. Content blocks are JSON in the API's wire format, so
/// the agent and the database never depend on SDK types, and tests fake this interface instead of calling Anthropic.
/// </summary>
public interface IChatModel
{
  /// <summary>
  /// Sends the conversation and streams the reply: a <see cref="ChatModelEvent.TextDelta"/> per piece of text as it
  /// arrives, then exactly one <see cref="ChatModelEvent.Completed"/> with the whole turn.
  /// </summary>
  /// <param name="request">The system prompt, tools and messages.</param>
  /// <param name="cancellationToken">Stops the call.</param>
  /// <returns>The reply's events.</returns>
  /// <exception cref="ChatModelException">The call failed; the message is safe to show the user.</exception>
  IAsyncEnumerable<ChatModelEvent> StreamAsync(ChatModelRequest request, CancellationToken cancellationToken);
}

/// <summary>One call to Claude.</summary>
/// <param name="System">The system prompt.</param>
/// <param name="Tools">The tools Claude may call.</param>
/// <param name="Messages">The conversation so far, oldest first, ending with a user turn.</param>
public record ChatModelRequest(string System, IReadOnlyList<AgentTool> Tools, IReadOnlyList<ChatTurn> Messages);

/// <summary>One message of the conversation.</summary>
/// <param name="Role"><c>user</c> or <c>assistant</c>.</param>
/// <param name="Content">Its content blocks in the API's wire format.</param>
public record ChatTurn(string Role, JsonArray Content);

/// <summary>A tool Claude may call, always sent with <c>strict: true</c>.</summary>
/// <param name="Name">The tool name.</param>
/// <param name="Description">When to call it and what it does.</param>
/// <param name="InputSchema">Its JSON Schema, an <c>object</c> within strict tool use's supported keywords.</param>
public record AgentTool(string Name, string Description, JsonObject InputSchema);

/// <summary>An event from <see cref="IChatModel.StreamAsync"/>.</summary>
public abstract record ChatModelEvent
{
  /// <summary>A piece of the reply's text, as it arrives.</summary>
  /// <param name="Text">The delta.</param>
  public sealed record TextDelta(string Text) : ChatModelEvent;

  /// <summary>The reply is complete.</summary>
  /// <param name="Content">
  /// The turn's content blocks, ready to send back as the next assistant message: what came before a refusal fallback
  /// is already reduced to its text, and fallback markers are removed.
  /// </param>
  /// <param name="StopReason">
  /// Why it stopped: <c>end_turn</c>, <c>tool_use</c>, <c>max_tokens</c>, <c>refusal</c>.
  /// </param>
  public sealed record Completed(JsonArray Content, string StopReason) : ChatModelEvent;
}

/// <summary>A call to Claude failed.</summary>
/// <param name="message">What went wrong, in words safe to show the user.</param>
/// <param name="innerException">The underlying error, for the API's logs.</param>
public class ChatModelException(string message, Exception? innerException = null) : Exception(message, innerException);
