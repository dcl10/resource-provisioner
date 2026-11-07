namespace ResourceProvisioner.Api.Models;

/// <summary>A conversation with its messages, as the chat page shows them.</summary>
/// <param name="Id">Conversation ID.</param>
/// <param name="OwnerId">Keycloak user ID (<c>sub</c>) of the owner.</param>
/// <param name="OwnerEmail">The owner's email when it was started, if known.</param>
/// <param name="Title">The start of its first message; null until one is sent.</param>
/// <param name="CreatedAt">When it was started.</param>
/// <param name="UpdatedAt">When a message was last added.</param>
/// <param name="Messages">What the user typed and what the agent said, oldest first.</param>
public record ConversationResponse(
  Guid Id,
  string OwnerId,
  string? OwnerEmail,
  string? Title,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt,
  IReadOnlyList<ChatMessageResponse> Messages);

/// <summary>
/// One message as the chat page shows it: the text, plus any requests the agent proposed in it. Tool calls, tool
/// results and thinking are left out.
/// </summary>
/// <param name="Id">The ID of the stored message it starts at.</param>
/// <param name="Role"><c>user</c> or <c>assistant</c>.</param>
/// <param name="Text">The text, as Markdown for the assistant.</param>
/// <param name="Proposals">Requests the agent proposed in this message.</param>
/// <param name="CreatedAt">When it was added.</param>
public record ChatMessageResponse(
  long Id,
  string Role,
  string Text,
  IReadOnlyList<ProposalResponse> Proposals,
  DateTimeOffset CreatedAt);
