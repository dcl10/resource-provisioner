namespace ResourceProvisioner.Api.ResourceCatalog;

/// <summary>One of a resource type's Terraform outputs.</summary>
public record OutputSpec
{
  /// <summary>The Terraform output name.</summary>
  public required string Name { get; init; }

  /// <summary>What the output is, for the user.</summary>
  public required string Description { get; init; }

  /// <summary>
  /// Whether the output is a secret (a password, a key). Sensitive outputs are stored apart from the others and never
  /// reach the LLM. Must match <c>sensitive = true</c> on the output in <c>outputs.tf</c>.
  /// </summary>
  public bool Sensitive { get; init; }
}
