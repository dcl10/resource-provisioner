namespace ResourceProvisioner.Api.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>Body of <c>POST /api/invitations</c>.</summary>
public class InviteRequest
{
  /// <summary>The colleague's email address. It becomes their username.</summary>
  [Required]
  [EmailAddress]
  [MaxLength(254)]
  public string Email { get; init; } = "";
}
