namespace ResourceProvisioner.Api.Models;

using System.ComponentModel.DataAnnotations;

/// <summary>The body of <c>POST /api/requests/confirm</c>: the requests to confirm together.</summary>
public record ConfirmRequestsRequest
{
  /// <summary>Request IDs, 1 to 20, each the user's own and awaiting confirmation.</summary>
  [Required]
  [MinLength(1)]
  [MaxLength(Services.Requests.ProvisioningRequestService.MaxConfirmAtOnce)]
  public required IReadOnlyList<Guid> RequestIds { get; init; }
}
