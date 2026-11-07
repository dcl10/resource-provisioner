namespace ResourceProvisioner.Api.Models;

/// <summary>A conversation in the conversation list.</summary>
/// <param name="Id">Conversation ID.</param>
/// <param name="OwnerId">Keycloak user ID (<c>sub</c>) of the owner.</param>
/// <param name="OwnerEmail">The owner's email when it was started, if known.</param>
/// <param name="Title">The start of its first message; null until one is sent.</param>
/// <param name="CreatedAt">When it was started.</param>
/// <param name="UpdatedAt">When a message was last added.</param>
public record ConversationSummaryResponse(
  Guid Id,
  string OwnerId,
  string? OwnerEmail,
  string? Title,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt);
