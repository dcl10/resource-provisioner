namespace ResourceProvisioner.Api.Models;

/// <summary>
/// One turn sent to or received from Claude, stored exactly as the Messages API takes it back so the conversation can
/// be replayed. Append-only: replaying an edited history would invalidate the thinking blocks in it.
/// </summary>
public class ConversationMessage
{
  /// <summary>The <c>user</c> role.</summary>
  public const string UserRole = "user";

  /// <summary>The <c>assistant</c> role.</summary>
  public const string AssistantRole = "assistant";

  /// <summary>Increasing ID; a conversation's messages are replayed in this order.</summary>
  public long Id { get; set; }

  /// <summary>The conversation it belongs to.</summary>
  public Guid ConversationId { get; set; }

  /// <summary><see cref="UserRole"/> (the user's text, or tool results) or <see cref="AssistantRole"/>.</summary>
  public required string Role { get; set; }

  /// <summary>
  /// The content blocks as a JSON array in the Messages API's wire format (<c>text</c>, <c>thinking</c>,
  /// <c>redacted_thinking</c>, <c>tool_use</c>, <c>tool_result</c>). Never holds a secret: tools return none.
  /// </summary>
  public required string Content { get; set; }

  /// <summary>When it was added.</summary>
  public DateTimeOffset CreatedAt { get; set; }
}
