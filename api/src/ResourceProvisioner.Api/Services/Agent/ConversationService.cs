namespace ResourceProvisioner.Api.Services.Agent;

using System.Text.Json;
using System.Text.Json.Nodes;
using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Requests;

/// <summary>Conversations: starting them, listing them and reading them back for the chat page.</summary>
public interface IConversationService
{
  /// <summary>Starts an empty conversation owned by the user.</summary>
  /// <param name="user">The signed-in user.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The new conversation.</returns>
  Task<ConversationSummaryResponse> CreateAsync(RequestUser user, CancellationToken cancellationToken = default);

  /// <summary>Lists the user's conversations, or everyone's for an admin, most recently active first.</summary>
  /// <param name="user">The signed-in user.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The conversations.</returns>
  Task<IReadOnlyList<ConversationSummaryResponse>> ListAsync(
    RequestUser user,
    CancellationToken cancellationToken = default);

  /// <summary>Reads a conversation with its messages as the chat page shows them.</summary>
  /// <param name="id">Conversation ID.</param>
  /// <param name="user">The signed-in user: the owner, or an admin.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The conversation.</returns>
  /// <exception cref="KeyNotFoundException">No such conversation the user can see.</exception>
  Task<ConversationResponse> GetAsync(Guid id, RequestUser user, CancellationToken cancellationToken = default);
}

/// <summary><see cref="IConversationService"/> on the app database.</summary>
/// <param name="db">The app database.</param>
/// <param name="time">The clock.</param>
public class ConversationService(AppDbContext db, TimeProvider time) : IConversationService
{
  private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

  /// <inheritdoc />
  public async Task<ConversationSummaryResponse> CreateAsync(
    RequestUser user,
    CancellationToken cancellationToken = default)
  {
    var now = time.GetUtcNow();
    var conversation = new Conversation
    {
      Id = Guid.NewGuid(), OwnerId = user.UserId, OwnerEmail = user.Email, CreatedAt = now, UpdatedAt = now,
    };
    db.Conversations.Add(conversation);
    await db.SaveChangesAsync(cancellationToken);
    return ToSummary(conversation);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<ConversationSummaryResponse>> ListAsync(
    RequestUser user,
    CancellationToken cancellationToken = default)
  {
    var query = db.Conversations.AsNoTracking();
    if (!user.IsAdmin)
    {
      query = query.Where(x => x.OwnerId == user.UserId);
    }

    var conversations = await query.OrderByDescending(x => x.UpdatedAt).ToListAsync(cancellationToken);
    return [.. conversations.Select(ToSummary)];
  }

  /// <inheritdoc />
  public async Task<ConversationResponse> GetAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default)
  {
    var conversation = await db.Conversations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    if (conversation is null || (conversation.OwnerId != user.UserId && !user.IsAdmin))
    {
      throw new KeyNotFoundException($"Conversation {id} doesn't exist.");
    }

    var messages = await db.ConversationMessages.AsNoTracking()
      .Where(x => x.ConversationId == id)
      .OrderBy(x => x.Id)
      .ToListAsync(cancellationToken);

    return new ConversationResponse(
      conversation.Id,
      conversation.OwnerId,
      conversation.OwnerEmail,
      conversation.Title,
      conversation.CreatedAt,
      conversation.UpdatedAt,
      ToDisplay(messages));
  }

  /// <summary>
  /// The stored turns as the chat page shows them: what the user typed, and each assistant reply's text with the
  /// requests proposed in it. Tool results, tool calls and thinking are left out; the consecutive assistant turns of
  /// one reply (split by tool calls) become one message.
  /// </summary>
  /// <param name="messages">The stored messages, oldest first.</param>
  /// <returns>The messages to show.</returns>
  public static IReadOnlyList<ChatMessageResponse> ToDisplay(IReadOnlyList<ConversationMessage> messages)
  {
    var shown = new List<ChatMessageResponse>();
    var proposeCalls = new HashSet<string>();
    foreach (var message in messages)
    {
      var content = JsonNode.Parse(message.Content)!.AsArray();
      if (message.Role == ConversationMessage.AssistantRole)
      {
        foreach (var call in ContentBlocks.OfType(content, "tool_use"))
        {
          if (call["name"]?.GetValue<string>() == AgentTools.ProposeRequest)
          {
            proposeCalls.Add(call["id"]!.GetValue<string>());
          }
        }

        AddToReply(shown, message, ContentBlocks.JoinedText(content), []);
        continue;
      }

      var userText = ContentBlocks.JoinedText(content);
      if (userText.Length > 0)
      {
        shown.Add(new ChatMessageResponse(message.Id, message.Role, userText, [], message.CreatedAt));
      }

      var proposals = ContentBlocks.OfType(content, "tool_result")
        .Where(r => r["is_error"]?.GetValue<bool>() != true &&
                    proposeCalls.Contains(r["tool_use_id"]?.GetValue<string>() ?? ""))
        .Select(r => ProposalFrom(r["content"]?.GetValue<string>()))
        .OfType<ProposalResponse>()
        .ToList();
      if (proposals.Count > 0)
      {
        AddToReply(shown, message, "", proposals);
      }
    }

    return [.. shown.Where(m => m.Text.Length > 0 || m.Proposals.Count > 0)];
  }

  private static void AddToReply(
    List<ChatMessageResponse> shown,
    ConversationMessage message,
    string text,
    IReadOnlyList<ProposalResponse> proposals)
  {
    if (shown.Count > 0 && shown[^1].Role == ConversationMessage.AssistantRole)
    {
      var reply = shown[^1];
      var joined = reply.Text.Length > 0 && text.Length > 0 ? $"{reply.Text}\n\n{text}" : reply.Text + text;
      shown[^1] = reply with { Text = joined, Proposals = [.. reply.Proposals, .. proposals] };
    }
    else
    {
      shown.Add(new ChatMessageResponse(
        message.Id,
        ConversationMessage.AssistantRole,
        text,
        proposals,
        message.CreatedAt));
    }
  }

  private static ProposalResponse? ProposalFrom(string? toolResult)
  {
    try
    {
      return toolResult is null
        ? null
        : JsonNode.Parse(toolResult)?["proposal"]?.Deserialize<ProposalResponse>(Json);
    }
    catch (JsonException)
    {
      return null;
    }
  }

  private static ConversationSummaryResponse ToSummary(Conversation x) =>
    new(x.Id, x.OwnerId, x.OwnerEmail, x.Title, x.CreatedAt, x.UpdatedAt);
}
