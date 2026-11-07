namespace ResourceProvisioner.Api.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>The body of <c>POST /api/conversations/{id}/messages</c>.</summary>
public record SendMessageRequest
{
  /// <summary>The longest message accepted, in characters.</summary>
  public const int MaxLength = 4000;

  /// <summary>What the user typed.</summary>
  [Required(AllowEmptyStrings = false)]
  [StringLength(MaxLength)]
  public required string Text { get; init; }
}
