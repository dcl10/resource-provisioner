namespace ResourceProvisioner.Api.Services.Agent;

using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Requests;
using ResourceCatalog;

/// <summary>Sends a user's message to the agent and streams the reply.</summary>
public interface IChatAgent
{
  /// <summary>
  /// Checks the conversation, then returns the reply as a stream: text deltas, a proposal for each request the agent
  /// proposes, and a final done or error event. Every turn is saved as it completes.
  /// </summary>
  /// <param name="conversationId">The conversation.</param>
  /// <param name="user">The signed-in user; must be its owner.</param>
  /// <param name="text">What the user typed.</param>
  /// <param name="cancellationToken">Cancels the check before the stream starts.</param>
  /// <returns>The events, to enumerate with the caller's own cancellation token.</returns>
  /// <exception cref="KeyNotFoundException">No such conversation the user can see.</exception>
  /// <exception cref="ConversationForbiddenException">An admin writing in someone else's conversation.</exception>
  Task<IAsyncEnumerable<ChatEvent>> SendAsync(
    Guid conversationId,
    RequestUser user,
    string text,
    CancellationToken cancellationToken = default);
}

/// <summary>
/// The user can see the conversation but can't write in it: an admin in someone else's, since the agent acts as the
/// conversation's owner. The API answers 403.
/// </summary>
/// <param name="message">Why.</param>
public class ConversationForbiddenException(string message) : Exception(message);

/// <summary>
/// <see cref="IChatAgent"/>: a manual tool-use loop over <see cref="IChatModel"/>. Each call to Claude streams its
/// text out; when it asks for tools, they run on the server and their results go back, until Claude ends its turn.
/// Messages are appended, never edited, so thinking blocks stay valid when replayed.
/// </summary>
/// <param name="db">The app database.</param>
/// <param name="model">Claude.</param>
/// <param name="tools">Runs tool calls.</param>
/// <param name="catalog">The catalog the tool definitions are generated from.</param>
/// <param name="time">The clock.</param>
public class ChatAgent(
  AppDbContext db,
  IChatModel model,
  AgentToolExecutor tools,
  ICatalogService catalog,
  TimeProvider time) : IChatAgent
{
  /// <summary>The longest conversation title, taken from the start of the first message.</summary>
  public const int TitleLength = 80;

  /// <summary>
  /// The most calls to Claude one user message can cause, so a model stuck calling tools can't run up a bill.
  /// </summary>
  public const int MaxModelCalls = 10;

  // One reply at a time per conversation: two interleaved replies would corrupt its history. The POC runs one API
  // instance, so a process-wide set is enough.
  private static readonly ConcurrentDictionary<Guid, byte> Busy = new();

  /// <inheritdoc />
  public async Task<IAsyncEnumerable<ChatEvent>> SendAsync(
    Guid conversationId,
    RequestUser user,
    string text,
    CancellationToken cancellationToken = default)
  {
    var conversation = await db.Conversations.SingleOrDefaultAsync(x => x.Id == conversationId, cancellationToken);
    if (conversation is null || (conversation.OwnerId != user.UserId && !user.IsAdmin))
    {
      throw new KeyNotFoundException($"Conversation {conversationId} doesn't exist.");
    }

    if (conversation.OwnerId != user.UserId)
    {
      throw new ConversationForbiddenException("Only the conversation's owner can send messages in it.");
    }

    return Reply(conversation, user, text, CancellationToken.None);
  }

  private async IAsyncEnumerable<ChatEvent> Reply(
    Conversation conversation,
    RequestUser user,
    string text,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    if (!Busy.TryAdd(conversation.Id, 0))
    {
      yield return ChatEvent.ForError("The agent is still replying in this conversation. Wait for it to finish.");
      yield break;
    }

    try
    {
      var history = await LoadHistory(conversation.Id, cancellationToken);
      conversation.Title ??= TitleFrom(text);
      history.Add(await Append(
        conversation,
        ConversationMessage.UserRole,
        [ContentBlocks.Text(text)],
        cancellationToken));

      var toolDefinitions = AgentTools.For(catalog);
      for (var call = 0; call < MaxModelCalls; call++)
      {
        ChatModelEvent.Completed? turn = null;
        string? error = null;
        await foreach (var (modelEvent, failure) in Guard(
                         model.StreamAsync(new ChatModelRequest(SystemPrompt.Text, toolDefinitions, history),
                           cancellationToken)))
        {
          switch (modelEvent)
          {
            case ChatModelEvent.TextDelta delta:
              yield return ChatEvent.ForText(delta.Text);
              break;
            case ChatModelEvent.Completed completed:
              turn = completed;
              break;
          }

          error ??= failure;
        }

        if (error is not null || turn is null)
        {
          yield return ChatEvent.ForError(error ?? "Claude's reply ended unexpectedly. Try again.");
          yield break;
        }

        // A refused or truncated turn isn't saved: replaying a partial turn (possibly with an unanswered tool call)
        // would break the conversation. The user's message stays, and their next one is read together with it.
        if (turn.StopReason == "refusal")
        {
          yield return ChatEvent.ForError(
            "Claude declined to answer that. Rephrase the request, or start a new conversation.");
          yield break;
        }

        if (turn.StopReason == "max_tokens")
        {
          yield return ChatEvent.ForError("Claude's reply was too long and was cut off. Try asking for less at once.");
          yield break;
        }

        if (turn.Content.Count > 0)
        {
          history.Add(await Append(
            conversation,
            ConversationMessage.AssistantRole,
            turn.Content,
            CancellationToken.None));
        }

        // Every tool_use needs a tool_result before the next call, whatever the stop reason says.
        var calls = ContentBlocks.OfType(turn.Content, "tool_use").ToList();
        if (calls.Count == 0)
        {
          yield return ChatEvent.Done;
          yield break;
        }

        // The tool calls are saved, so their results must be too, or every later call in this conversation fails.
        // Hence no cancellation and no yield (where a disconnected client stops the iterator) until they are.
        var results = new JsonArray();
        var proposals = new List<ProposalResponse>();
        foreach (var toolCall in calls)
        {
          var outcome = await tools.ExecuteAsync(
            toolCall["name"]!.GetValue<string>(),
            toolCall["input"]?.AsObject() ?? [],
            user,
            conversation.Id,
            CancellationToken.None);
          results.Add(ContentBlocks.ToolResult(toolCall["id"]!.GetValue<string>(), outcome.Content, outcome.IsError));
          if (outcome.Proposal is { } proposal)
          {
            proposals.Add(proposal);
          }
        }

        history.Add(await Append(conversation, ConversationMessage.UserRole, results, CancellationToken.None));
        foreach (var proposal in proposals)
        {
          yield return ChatEvent.ForProposal(proposal);
        }
      }

      yield return ChatEvent.ForError(
        "The agent took too many steps on that message and stopped. Try a simpler request.");
    }
    finally
    {
      Busy.TryRemove(conversation.Id, out _);
    }
  }

  /// <summary>Passes the model's events through, turning a <see cref="ChatModelException"/> into a failure.</summary>
  private static async IAsyncEnumerable<(ChatModelEvent? Event, string? Failure)> Guard(
    IAsyncEnumerable<ChatModelEvent> source)
  {
    await using var events = source.GetAsyncEnumerator();
    while (true)
    {
      string? failure = null;
      var hasNext = false;
      try
      {
        hasNext = await events.MoveNextAsync();
      }
      catch (ChatModelException ex)
      {
        failure = ex.Message;
      }

      if (failure is not null)
      {
        yield return (null, failure);
        yield break;
      }

      if (!hasNext)
      {
        yield break;
      }

      yield return (events.Current, null);
    }
  }

  private async Task<List<ChatTurn>> LoadHistory(Guid conversationId, CancellationToken cancellationToken)
  {
    var messages = await db.ConversationMessages.AsNoTracking()
      .Where(x => x.ConversationId == conversationId)
      .OrderBy(x => x.Id)
      .ToListAsync(cancellationToken);
    return [.. messages.Select(m => new ChatTurn(m.Role, JsonNode.Parse(m.Content)!.AsArray()))];
  }

  private async Task<ChatTurn> Append(
    Conversation conversation,
    string role,
    JsonArray content,
    CancellationToken cancellationToken)
  {
    var now = time.GetUtcNow();
    var message = new ConversationMessage
    {
      ConversationId = conversation.Id, Role = role, Content = content.ToJsonString(), CreatedAt = now,
    };
    db.ConversationMessages.Add(message);
    conversation.UpdatedAt = now;
    await db.SaveChangesAsync(cancellationToken);
    db.Entry(message).State = EntityState.Detached;
    return new ChatTurn(role, content);
  }

  private static string TitleFrom(string text)
  {
    var line = text.Trim().ReplaceLineEndings(" ");
    return line.Length <= TitleLength ? line : $"{line[..(TitleLength - 1)].TrimEnd()}…";
  }
}
