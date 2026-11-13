namespace ResourceProvisioner.Api.Models;

/// <summary>
/// A request to provision one resource from the catalog, owned by the user it was proposed for. Created by the
/// agent's <c>propose_request</c> tool in <see cref="RequestStatus.AwaitingConfirmation"/>; only the owner's confirm
/// starts provisioning. Change <see cref="Status"/> only through <c>RequestStateMachine</c>.
/// </summary>
public class ProvisioningRequest
{
  /// <summary>
  /// Unique identifier, never reused. Rendered as a lowercase GUID (<c>Guid.ToString()</c>), which is what the
  /// <c>rp-&lt;requestId&gt;</c> tag and the manifests' droplet-reference patterns expect.
  /// </summary>
  public Guid Id { get; set; }

  /// <summary>Keycloak user ID (<c>sub</c>) of the owner. Only the owner can confirm, cancel or reference it.</summary>
  public required string OwnerId { get; set; }

  /// <summary>The owner's email when the request was proposed, so admins can see whose it is.</summary>
  public string? OwnerEmail { get; set; }

  /// <summary>The resource type's ID in the catalog, such as <c>droplet</c>.</summary>
  public required string Type { get; set; }

  /// <summary>
  /// The validated variables with defaults applied, as a JSON object: what becomes <c>terraform.tfvars.json</c>,
  /// apart from the variables the runner sets.
  /// </summary>
  public required string Variables { get; set; }

  /// <summary>
  /// The exact <c>terraform.tfvars.json</c> its apply used (<see cref="Variables"/> with the runner's variables laid
  /// over them), saved before Terraform first runs, so destroy can rebuild the workspace from the catalog. Null until
  /// the runner applies it. Holds sensitive variables, like <see cref="Variables"/>, so it's never returned by an
  /// endpoint or shown to the agent.
  /// </summary>
  public string? TerraformVariables { get; set; }

  /// <summary>Where the request has got to. Also the concurrency token, so two transitions can't both win.</summary>
  public RequestStatus Status { get; set; }

  /// <summary>The non-sensitive Terraform outputs as a JSON object, once provisioned.</summary>
  public string? Outputs { get; set; }

  /// <summary>
  /// The project it belongs to. Required for every type but <c>project</c> (the service enforces it, so a request
  /// is checked when it's proposed and confirmed); null on a <c>project</c> request, which backs a project instead
  /// (<see cref="Project.ProjectRequestId"/>).
  /// </summary>
  public Guid? ProjectId { get; set; }

  /// <summary>The chat conversation the agent proposed it in, if any.</summary>
  public Guid? ConversationId { get; set; }

  /// <summary>When it was proposed.</summary>
  public DateTimeOffset CreatedAt { get; set; }

  /// <summary>When its status last changed.</summary>
  public DateTimeOffset UpdatedAt { get; set; }

  /// <summary>When the owner confirmed it, if they have.</summary>
  public DateTimeOffset? ConfirmedAt { get; set; }

  /// <summary>
  /// The sensitive Terraform outputs, kept in their own table so listing or reading requests never loads them.
  /// </summary>
  public RequestSensitiveOutputs? SensitiveOutputs { get; set; }
}
