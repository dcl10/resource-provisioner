namespace ResourceProvisioner.Api.Models;

/// <summary>
/// A user's project: the top level that holds their resources together. Every request belongs to exactly one
/// project (<see cref="ProvisioningRequest.ProjectId"/>), except the <c>project</c> request that backs it
/// (<see cref="ProjectRequestId"/>), which creates the DigitalOcean project and gives it its status, logs, state and
/// outputs. Created with that request, when it's proposed. Once that request is cancelled or destroyed, the project
/// is gone.
/// </summary>
public class Project
{
  /// <summary>
  /// The catalog type whose requests back projects. Its manifest's <c>name</c>, <c>description</c>,
  /// <c>purpose</c> and <c>environment</c> variables become the project's, and its <c>id</c> output is the
  /// DigitalOcean project ID the runner passes to the requests in it.
  /// </summary>
  public const string RequestType = "project";

  /// <summary>Unique identifier, never reused. Not the same as <see cref="ProjectRequestId"/>.</summary>
  public Guid Id { get; set; }

  /// <summary>Keycloak user ID (<c>sub</c>) of the owner, the same as its request's.</summary>
  public required string OwnerId { get; set; }

  /// <summary>The owner's email when the project was proposed, so admins can see whose it is.</summary>
  public string? OwnerEmail { get; set; }

  /// <summary>The project's name: its request's <c>name</c> variable.</summary>
  public required string Name { get; set; }

  /// <summary>Its request's <c>description</c> variable, if set.</summary>
  public string? Description { get; set; }

  /// <summary>Its request's <c>purpose</c> variable, if set.</summary>
  public string? Purpose { get; set; }

  /// <summary>Its request's <c>environment</c> variable, if set.</summary>
  public string? Environment { get; set; }

  /// <summary>When it was proposed.</summary>
  public DateTimeOffset CreatedAt { get; set; }

  /// <summary>The <c>project</c> request that backs it, and that its status comes from.</summary>
  public Guid ProjectRequestId { get; set; }
}
