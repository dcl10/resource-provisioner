namespace ResourceProvisioner.Api.Config;

/// <summary>Credentials Terraform uses to manage DigitalOcean resources.</summary>
public class DigitalOceanOptions
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "DigitalOcean";

  /// <summary>DigitalOcean API token. Secret, and never passed to the LLM.</summary>
  public string Token { get; init; } = "";
}
