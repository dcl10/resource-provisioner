namespace ResourceProvisioner.Api.Config;

using System.ComponentModel.DataAnnotations;

/// <summary>Where the resource catalog (one <c>&lt;type&gt;/</c> folder per resource type) lives.</summary>
public class CatalogOptions
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "Catalog";

  /// <summary>
  /// Directory holding one folder per resource type, each with a Terraform module and a <c>resource.json</c>
  /// manifest. An absolute path is used as-is. A relative path is looked for in the content root (the project folder
  /// under <c>dotnet run</c>) and then in each folder above it, so the default, <c>catalog</c>, finds the repo's
  /// <c>catalog/</c> wherever in the repo the API starts.
  /// </summary>
  [Required]
  public string Root { get; init; } = "catalog";
}
