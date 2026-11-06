namespace ResourceProvisioner.Api.ResourceCatalog;

using System.Text.Json.Nodes;

/// <summary>The resource types the agent can provision, loaded from the catalog's manifests at startup.</summary>
public interface ICatalogService
{
  /// <summary>Every resource type, ordered by ID.</summary>
  IReadOnlyList<ResourceManifest> Types { get; }

  /// <summary>Finds a resource type by ID.</summary>
  /// <param name="type">The type's ID, such as <c>droplet</c>.</param>
  /// <returns>Its manifest, or null if there's no such type.</returns>
  ResourceManifest? Find(string type);

  /// <summary>The folder holding a resource type's Terraform module, for the runner to copy.</summary>
  /// <param name="type">The type's ID.</param>
  /// <returns>The folder's full path.</returns>
  /// <exception cref="KeyNotFoundException">There's no such type.</exception>
  string GetModuleDirectory(string type);

  /// <summary>
  /// Checks a request's variables against the type's manifest: types, required variables, allowed values, patterns,
  /// ranges and unknown names. Applies defaults, including those of nested object fields.
  /// </summary>
  /// <param name="type">The type's ID. An unknown type is reported as an error on <c>type</c>.</param>
  /// <param name="values">The variables, by name. Null or a missing name means not given.</param>
  /// <returns>The values with defaults applied, and any errors keyed by field path.</returns>
  VariableValidationResult ValidateVariables(string type, JsonObject? values);
}
