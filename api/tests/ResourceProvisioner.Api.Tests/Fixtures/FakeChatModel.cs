namespace ResourceProvisioner.Api.Tests.Fixtures;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Services.Agent;

/// <summary>
/// Stands in for Claude: plays back scripted replies, one per call, and records every request. A call with nothing
/// scripted replies "OK." and ends its turn.
/// </summary>
public class FakeChatModel : IChatModel
{
  private readonly ConcurrentQueue<Func<ChatModelRequest, IReadOnlyList<ChatModelEvent>>> _replies = new();

  /// <summary>Every request, in order, with the messages copied as they were when sent.</summary>
  public ConcurrentQueue<ChatModelRequest> Requests { get; } = new();

  /// <summary>Forgets scripted replies and recorded requests.</summary>
  public void Reset()
  {
    _replies.Clear();
    Requests.Clear();
  }

  /// <summary>Scripts a reply that streams <paramref name="text"/> in two pieces, then ends the turn.</summary>
  /// <param name="text">The reply.</param>
  public void ReplyText(string text)
  {
    var half = text.Length / 2;
    Reply(
      new ChatModelEvent.TextDelta(text[..half]),
      new ChatModelEvent.TextDelta(text[half..]),
      new ChatModelEvent.Completed(new JsonArray(ContentBlocks.Text(text)), "end_turn"));
  }

  /// <summary>Scripts a reply that calls one tool, optionally saying something first.</summary>
  /// <param name="name">The tool.</param>
  /// <param name="input">Its input as JSON.</param>
  /// <param name="id">The call's ID.</param>
  /// <param name="text">Text before the call, if any.</param>
  public void ReplyToolUse(string name, string input = "{}", string id = "toolu_1", string? text = null)
  {
    var content = new JsonArray();
    var events = new List<ChatModelEvent>();
    if (text is not null)
    {
      content.Add(ContentBlocks.Text(text));
      events.Add(new ChatModelEvent.TextDelta(text));
    }

    content.Add(ContentBlocks.ToolUse(id, name, JsonNode.Parse(input)!.AsObject()));
    events.Add(new ChatModelEvent.Completed(content, "tool_use"));
    Reply([.. events]);
  }

  /// <summary>Scripts a reply made of exactly these events.</summary>
  /// <param name="events">The events.</param>
  public void Reply(params ChatModelEvent[] events) => _replies.Enqueue(_ => events);

  /// <summary>Scripts a call that fails with a <see cref="ChatModelException"/>.</summary>
  /// <param name="message">The user-facing message.</param>
  public void Fail(string message) => _replies.Enqueue(_ => throw new ChatModelException(message));

  /// <inheritdoc />
  public async IAsyncEnumerable<ChatModelEvent> StreamAsync(
    ChatModelRequest request,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    // The agent keeps adding to the list it passes in, so record a copy.
    var messages = request.Messages.Select(m => m with { Content = (JsonArray)m.Content.DeepClone() }).ToList();
    Requests.Enqueue(request with { Messages = messages });
    var events = _replies.TryDequeue(out var reply)
      ? reply(request)
      : [new ChatModelEvent.Completed(new JsonArray(ContentBlocks.Text("OK.")), "end_turn")];

    foreach (var e in events)
    {
      await Task.Yield();
      yield return e;
    }
  }
}
