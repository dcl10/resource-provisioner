namespace ResourceProvisioner.Api.Models;

using System.Text.Json.Nodes;

/// <summary>
/// A provisioning request as the requests list and detail pages show it. Never carries sensitive variables or
/// sensitive outputs.
/// </summary>
/// <param name="Id">Request ID, a lowercase GUID.</param>
/// <param name="OwnerId">Keycloak user ID (<c>sub</c>) of the owner.</param>
/// <param name="OwnerEmail">The owner's email when it was proposed, if known.</param>
/// <param name="Type">The resource type's ID.</param>
/// <param name="DisplayName">The resource type's display name, or the ID if the type is no longer in the catalog.</param>
/// <param name="Status">Where it has got to.</param>
/// <param name="Variables">The variables, with defaults applied and sensitive ones left out.</param>
/// <param name="Outputs">The non-sensitive outputs, once provisioned.</param>
/// <param name="Project">
/// The project it belongs to; for a <c>project</c> request, the project it backs. Null only for a request from before
/// projects, or one whose project is missing.
/// </param>
/// <param name="ConversationId">The conversation it was proposed in, if any.</param>
/// <param name="CreatedAt">When it was proposed.</param>
/// <param name="UpdatedAt">When its status last changed.</param>
/// <param name="ConfirmedAt">When the owner confirmed it, if they have.</param>
/// <param name="DependsOn">The requests it references, which are provisioned before it.</param>
/// <param name="Dependents">
/// The requests that depend on it (reference it, or are in the project it backs): destroying it destroys them first.
/// </param>
/// <param name="DestroyStoppedBy">
/// When its destroy stopped because something that depends on it failed to destroy, and it went back to the status it
/// had: that request, while it still exists. Null otherwise, and once it's destroyed again.
/// </param>
public record ProvisioningRequestResponse(
  Guid Id,
  string OwnerId,
  string? OwnerEmail,
  string Type,
  string DisplayName,
  RequestStatus Status,
  JsonObject Variables,
  JsonObject? Outputs,
  ProjectLink? Project,
  Guid? ConversationId,
  DateTimeOffset CreatedAt,
  DateTimeOffset UpdatedAt,
  DateTimeOffset? ConfirmedAt,
  IReadOnlyList<RequestLink> DependsOn,
  IReadOnlyList<RequestLink> Dependents,
  RequestLink? DestroyStoppedBy = null);
