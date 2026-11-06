namespace ResourceProvisioner.Api.Config;

using System.ComponentModel.DataAnnotations;

/// <summary>Where the Terraform runner keeps each request's working directory.</summary>
public class WorkspacesOptions
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "Workspaces";

  /// <summary>
  /// Directory holding one <c>&lt;requestId&gt;/</c> folder per request (module copy, tfvars and state). A relative
  /// path is resolved from the working directory.
  /// </summary>
  [Required]
  public string Root { get; init; } = "workspaces";
}
