namespace ResourceProvisioner.Api.Config;

using System.ComponentModel.DataAnnotations;

/// <summary>How the Terraform runner runs the CLI and how the logs endpoint follows a job.</summary>
public class RunnerOptions
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "Runner";

  /// <summary>The Terraform CLI: a path, or a command name looked up on <c>PATH</c>.</summary>
  [Required]
  public string TerraformPath { get; init; } = "terraform";

  /// <summary>How often the logs endpoint checks for new lines while a job runs.</summary>
  [Range(typeof(TimeSpan), "00:00:00.010", "00:01:00")]
  public TimeSpan LogPollInterval { get; init; } = TimeSpan.FromSeconds(1);
}
