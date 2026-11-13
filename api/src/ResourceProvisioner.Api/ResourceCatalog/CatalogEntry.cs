namespace ResourceProvisioner.Api.ResourceCatalog;

/// <summary>A loaded resource type: its manifest and the folder holding its Terraform module.</summary>
/// <param name="Manifest">The parsed and checked <c>resource.json</c>.</param>
/// <param name="ModuleDirectory">Full path to the type's folder.</param>
/// <param name="ModuleVariables">
/// Every variable the module's <c>variables.tf</c> declares, including those the runner sets (such as
/// <c>rp_project_id</c>), so the runner passes only what the module takes. Empty when not read.
/// </param>
public record CatalogEntry(
  ResourceManifest Manifest,
  string ModuleDirectory,
  IReadOnlySet<string>? ModuleVariables = null);
