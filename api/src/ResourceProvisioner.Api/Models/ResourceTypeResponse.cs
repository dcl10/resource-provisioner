namespace ResourceProvisioner.Api.Models;

using ResourceCatalog;

/// <summary>A resource type in the catalog, as its manifest describes it.</summary>
/// <param name="Id">The type's ID, such as <c>droplet</c>.</param>
/// <param name="DisplayName">A human name.</param>
/// <param name="Description">What the resource is and when to pick it.</param>
/// <param name="CostHint">A rough cost.</param>
/// <param name="Variables">The variables a request can set, with their types and constraints.</param>
/// <param name="Outputs">What the resource reports once provisioned, with which outputs are secrets.</param>
public record ResourceTypeResponse(
  string Id,
  string DisplayName,
  string Description,
  string CostHint,
  IReadOnlyList<VariableSpec> Variables,
  IReadOnlyList<OutputSpec> Outputs)
{
  /// <summary>Builds the response from a manifest.</summary>
  /// <param name="manifest">The type's manifest.</param>
  /// <returns>The response.</returns>
  public static ResourceTypeResponse From(ResourceManifest manifest) =>
    new(manifest.Id, manifest.DisplayName, manifest.Description, manifest.CostHint, manifest.Variables,
      manifest.Outputs);
}
