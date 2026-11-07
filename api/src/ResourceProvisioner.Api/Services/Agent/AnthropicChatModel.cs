namespace ResourceProvisioner.Api.Services.Agent;

using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Config;
using Microsoft.Extensions.Options;

/// <summary>
/// <see cref="IChatModel"/> on the Anthropic Messages API (the official <c>Anthropic</c> SDK), streaming. Uses the
/// beta endpoint for server-side refusal fallback (<c>fallbacks: "default"</c>), sends every tool with
/// <c>strict: true</c>, and caches the system prompt and tools across turns.
/// </summary>
/// <param name="client">The Anthropic client.</param>
/// <param name="options">The model, effort and API key.</param>
/// <param name="logger">Logs API errors, which the user sees only as a short message.</param>
public partial class AnthropicChatModel(
  AnthropicClient client,
  IOptions<AnthropicOptions> options,
  ILogger<AnthropicChatModel> logger) : IChatModel
{
  /// <summary>The beta that turns on server-side refusal fallback.</summary>
  public const string FallbackBeta = "server-side-fallback-2026-07-01";

  /// <summary>
  /// The reply's token ceiling. Chat replies are short, so this is rarely reached; it isn't a cost (only generated
  /// tokens are billed), and hitting it truncates the reply.
  /// </summary>
  public const int MaxTokens = 16000;

  private readonly AnthropicOptions _options = options.Value;

  /// <inheritdoc />
  public async IAsyncEnumerable<ChatModelEvent> StreamAsync(
    ChatModelRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(_options.ApiKey))
    {
      throw new ChatModelException("The API has no Anthropic API key (Anthropic:ApiKey), so the agent can't reply.");
    }

    var parameters = BuildParams(request);
    var events = new List<BetaRawMessageStreamEvent>();

    // A yield can't sit inside a try with a catch, so step the stream by hand to translate SDK errors.
    await using var stream = client.Beta.Messages.CreateStreaming(parameters, cancellationToken)
      .GetAsyncEnumerator(cancellationToken);
    while (true)
    {
      try
      {
        if (!await stream.MoveNextAsync())
        {
          break;
        }
      }
      catch (Exception ex) when (ex is AnthropicException or HttpRequestException)
      {
        throw Translate(ex);
      }

      var current = stream.Current;
      events.Add(current);
      if (current.TryPickContentBlockDelta(out var delta) && delta.Delta.TryPickText(out var text))
      {
        yield return new ChatModelEvent.TextDelta(text.Text);
      }
    }

    BetaMessage message;
    try
    {
      message = await events.ToAsyncEnumerable().Aggregate();
    }
    catch (AnthropicException ex)
    {
      throw Translate(ex);
    }

    yield return new ChatModelEvent.Completed(ToBlocks(message.Content), StopReasonOf(message));
  }

  /// <summary>
  /// The turn's blocks as they go back to the API next time. After a mid-output fallback, blocks before the last
  /// <c>fallback</c> marker keep only their text (the API's echo rule), and the markers themselves are dropped.
  /// </summary>
  /// <param name="content">The response's content.</param>
  /// <returns>The blocks in wire format.</returns>
  public static JsonArray ToBlocks(IReadOnlyList<BetaContentBlock> content)
  {
    var lastFallback = -1;
    for (var i = 0; i < content.Count; i++)
    {
      if (content[i].TryPickFallback(out _))
      {
        lastFallback = i;
      }
    }

    var blocks = new JsonArray();
    for (var i = 0; i < content.Count; i++)
    {
      var block = content[i];
      if (block.TryPickText(out var text))
      {
        blocks.Add(ContentBlocks.Text(text.Text));
      }
      else if (i < lastFallback)
      {
        // Before the boundary only text goes back.
      }
      else if (block.TryPickThinking(out var thinking))
      {
        blocks.Add(new JsonObject
        {
          ["type"] = "thinking", ["thinking"] = thinking.Thinking, ["signature"] = thinking.Signature,
        });
      }
      else if (block.TryPickRedactedThinking(out var redacted))
      {
        blocks.Add(new JsonObject { ["type"] = "redacted_thinking", ["data"] = redacted.Data });
      }
      else if (block.TryPickToolUse(out var toolUse))
      {
        var input = new JsonObject();
        foreach (var (key, value) in toolUse.Input)
        {
          input[key] = JsonNode.Parse(value.GetRawText());
        }

        blocks.Add(ContentBlocks.ToolUse(toolUse.ID, toolUse.Name, input));
      }
    }

    return blocks;
  }

  /// <summary>The request for one call: model, effort, fallback, cache control, strict tools and messages.</summary>
  /// <param name="request">The call.</param>
  /// <returns>The SDK parameters.</returns>
  public MessageCreateParams BuildParams(ChatModelRequest request) =>
    new()
    {
      Model = _options.Model,
      MaxTokens = MaxTokens,
      Betas = [FallbackBeta],
      Fallbacks = new Default(),
      OutputConfig = new BetaOutputConfig { Effort = EffortOf(_options.Effort) },

      // Caches tools and the system prompt (and the conversation so far) between turns.
      CacheControl = new BetaCacheControlEphemeral(),
      System = request.System,
      Tools = [.. request.Tools.Select(ToTool)],
      Messages = [.. request.Messages.Select(ToMessage)],
    };

  private static BetaToolUnion ToTool(AgentTool tool) =>
    new BetaTool
    {
      Name = tool.Name,
      Description = tool.Description,
      Strict = true,
      InputSchema = InputSchema.FromRawUnchecked(ToElements(tool.InputSchema)),
    };

  private static BetaMessageParam ToMessage(ChatTurn turn)
  {
    List<BetaContentBlockParam> blocks = [.. turn.Content.OfType<JsonObject>().Select(ToBlockParam)];
    return turn.Role == Models.ConversationMessage.AssistantRole
      ? new BetaAssistantMessageParam { Content = blocks }
      : new BetaUserMessageParam { Content = blocks };
  }

  private static BetaContentBlockParam ToBlockParam(JsonObject block) =>
    ContentBlocks.TypeOf(block) switch
    {
      "text" => new BetaTextBlockParam { Text = Str(block, "text") },
      "thinking" => new BetaThinkingBlockParam
      {
        Thinking = Str(block, "thinking"), Signature = Str(block, "signature"),
      },
      "redacted_thinking" => new BetaRedactedThinkingBlockParam { Data = Str(block, "data") },
      "tool_use" => new BetaToolUseBlockParam
      {
        ID = Str(block, "id"),
        Name = Str(block, "name"),
        Input = ToElements(block["input"]?.AsObject() ?? []),
      },
      "tool_result" => new BetaToolResultBlockParam
      {
        ToolUseID = Str(block, "tool_use_id"),
        Content = Str(block, "content"),
        IsError = block["is_error"]?.GetValue<bool>() ?? false,
      },
      var type => throw new InvalidOperationException($"Unexpected stored content block type '{type}'."),
    };

  private static string Str(JsonObject block, string name) => block[name]?.GetValue<string>() ?? "";

  private static Dictionary<string, JsonElement> ToElements(JsonObject json) =>
    json.ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));

  private static Effort EffortOf(string effort) =>
    effort switch
    {
      "medium" => Effort.Medium,
      "high" => Effort.High,
      "xhigh" => Effort.Xhigh,
      "max" => Effort.Max,
      _ => Effort.Low,
    };

  private static string StopReasonOf(BetaMessage message) =>
    message.StopReason?.Raw() ?? "end_turn";

  private ChatModelException Translate(Exception ex)
  {
    LogApiError(logger, ex);
    return ex switch
    {
      AnthropicRateLimitException =>
        new ChatModelException("Claude is busy right now (rate limited). Wait a minute and try again.", ex),
      Anthropic5xxException =>
        new ChatModelException("Claude is having problems right now. Try again shortly.", ex),
      AnthropicUnauthorizedException or AnthropicForbiddenException =>
        new ChatModelException("The API's Anthropic API key was rejected; an admin needs to fix it.", ex),
      AnthropicIOException or HttpRequestException =>
        new ChatModelException("Couldn't reach Claude. Try again shortly.", ex),
      _ => new ChatModelException("Claude couldn't answer that. Try again, or start a new conversation.", ex),
    };
  }

  [LoggerMessage(Level = LogLevel.Error, Message = "The Anthropic API call failed")]
  private static partial void LogApiError(ILogger logger, Exception exception);
}
