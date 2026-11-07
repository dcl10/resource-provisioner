namespace ResourceProvisioner.Api.Controllers;

using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Auth;
using Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Models;
using Services.Agent;
using Services.Requests;

/// <summary>
/// Conversations with the agent. Users see and write in their own; admins can read everyone's but write only in
/// their own, since the agent acts as the conversation's owner. 404 for a conversation the user can't see, 403 for an
/// admin writing in someone else's.
/// </summary>
/// <param name="conversations">The conversation service.</param>
/// <param name="agent">The agent.</param>
/// <param name="currentUser">The signed-in user.</param>
[ApiController]
[Route("api/conversations")]
public class ConversationsController(
  IConversationService conversations,
  IChatAgent agent,
  ICurrentUser currentUser) : ControllerBase
{
  private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);

  private RequestUser Caller => RequestUser.From(currentUser);

  /// <summary>Starts an empty conversation.</summary>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>201 with the conversation.</returns>
  [HttpPost]
  public async Task<ActionResult<ConversationSummaryResponse>> Create(CancellationToken cancellationToken)
  {
    var conversation = await conversations.CreateAsync(Caller, cancellationToken);
    return CreatedAtAction(nameof(Get), new { id = conversation.Id }, conversation);
  }

  /// <summary>Lists the user's conversations, or everyone's for an admin, most recently active first.</summary>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The conversations.</returns>
  [HttpGet]
  public async Task<ActionResult<IReadOnlyList<ConversationSummaryResponse>>> List(
    CancellationToken cancellationToken) =>
    Ok(await conversations.ListAsync(Caller, cancellationToken));

  /// <summary>Reads a conversation with its messages and the requests proposed in it.</summary>
  /// <param name="id">Conversation ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The conversation, or 404.</returns>
  [HttpGet("{id:guid}")]
  public async Task<ActionResult<ConversationResponse>> Get(Guid id, CancellationToken cancellationToken)
  {
    try
    {
      return await conversations.GetAsync(id, Caller, cancellationToken);
    }
    catch (KeyNotFoundException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
  }

  /// <summary>
  /// Sends a message and streams the agent's reply as server-sent events: <c>text</c> (<c>{"text"}</c>) for each piece
  /// of the reply, <c>proposal</c> (the confirmation card:
  /// <c>{"requestId","type","displayName","costHint","items"}</c>) for each request proposed, then <c>done</c>
  /// (<c>{}</c>) or <c>error</c> (<c>{"error"}</c>), after which the stream closes. Rate limited per user
  /// (<c>Limits:ChatMessagesPerMinute</c>; 429 when exceeded).
  /// </summary>
  /// <param name="id">Conversation ID.</param>
  /// <param name="body">The message.</param>
  /// <param name="cancellationToken">Cancels the request; closing the connection stops the reply.</param>
  /// <returns>The event stream, or a 403 or 404 problem.</returns>
  [HttpPost("{id:guid}/messages")]
  [EnableRateLimiting(AgentServiceCollectionExtensions.ChatRateLimitPolicy)]
  [Produces("text/event-stream")]
  public async Task<IResult> SendMessage(Guid id, SendMessageRequest body, CancellationToken cancellationToken)
  {
    try
    {
      var events = await agent.SendAsync(id, Caller, body.Text.Trim(), cancellationToken);
      return TypedResults.ServerSentEvents(ToSse(events, cancellationToken));
    }
    catch (KeyNotFoundException ex)
    {
      return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
    catch (ConversationForbiddenException ex)
    {
      return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
    }
  }

  private static async IAsyncEnumerable<SseItem<string>> ToSse(
    IAsyncEnumerable<ChatEvent> events,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    await foreach (var e in events.WithCancellation(cancellationToken))
    {
      yield return e.Kind switch
      {
        ChatEventKind.Text => Item("text", new { text = e.Text }),
        ChatEventKind.Proposal => Item("proposal", e.Proposal),
        ChatEventKind.Done => Item("done", new { }),
        _ => Item("error", new { error = e.Error }),
      };
    }
  }

  private static SseItem<string> Item(string type, object? data) =>
    new(JsonSerializer.Serialize(data, EventJson), type);
}
