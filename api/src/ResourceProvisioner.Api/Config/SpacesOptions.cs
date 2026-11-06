namespace ResourceProvisioner.Api.Config;

/// <summary>Spaces access keys, needed by Terraform to manage Spaces buckets.</summary>
public class SpacesOptions
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "Spaces";

  /// <summary>Spaces access key ID. Never passed to the LLM.</summary>
  public string AccessId { get; init; } = "";

  /// <summary>Spaces secret key. Secret, and never passed to the LLM.</summary>
  public string SecretKey { get; init; } = "";
}
