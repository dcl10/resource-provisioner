namespace ResourceProvisioner.Api.Config;

using System.ComponentModel.DataAnnotations;

/// <summary>Settings for calling Claude through the Anthropic API.</summary>
public class AnthropicOptions
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "Anthropic";

  /// <summary>Anthropic API key. Secret: set it in user-secrets or the environment, never in appsettings.</summary>
  public string ApiKey { get; init; } = "";

  /// <summary>Claude model ID. Defaults to Sonnet, because Opus uses too many tokens for this POC.</summary>
  [Required]
  public string Model { get; init; } = "claude-sonnet-5-5";

  /// <summary>How much effort Claude spends per turn (<c>output_config.effort</c>). Low keeps chat cheap.</summary>
  [AllowedValues("low", "medium", "high", "xhigh", "max")]
  public string Effort { get; init; } = "low";
}
