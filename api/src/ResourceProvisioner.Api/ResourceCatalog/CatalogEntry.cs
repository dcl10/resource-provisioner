namespace ResourceProvisioner.Api.ResourceCatalog;

/// <summary>A loaded resource type: its manifest and the folder holding its Terraform module.</summary>
/// <param name="Manifest">The parsed and checked <c>resource.json</c>.</param>
/// <param name="ModuleDirectory">Full path to the type's folder.</param>
public record CatalogEntry(ResourceManifest Manifest, string ModuleDirectory);
