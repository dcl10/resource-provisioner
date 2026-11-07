namespace ResourceProvisioner.Api.Models;

/// <summary>A chat with the agent, owned by the user who started it.</summary>
public class Conversation
{
  /// <summary>Unique identifier; requests proposed in it record it as their <c>ConversationId</c>.</summary>
  public Guid Id { get; set; }

  /// <summary>Keycloak user ID (<c>sub</c>) of the owner, the only user who can send messages in it.</summary>
  public required string OwnerId { get; set; }

  /// <summary>The owner's email when it was started, so admins can see whose it is.</summary>
  public string? OwnerEmail { get; set; }

  /// <summary>The start of the first message, for the conversation list; null until one is sent.</summary>
  public string? Title { get; set; }

  /// <summary>When it was started.</summary>
  public DateTimeOffset CreatedAt { get; set; }

  /// <summary>When a message was last added.</summary>
  public DateTimeOffset UpdatedAt { get; set; }
}
