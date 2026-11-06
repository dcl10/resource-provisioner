namespace ResourceProvisioner.Api.Config;

using System.ComponentModel.DataAnnotations;

/// <summary>Caps that keep LLM and DigitalOcean spend bounded.</summary>
public class LimitsOptions
{
  /// <summary>Configuration section these options bind to.</summary>
  public const string SectionName = "Limits";

  /// <summary>Most requests one user can have queued, applying or provisioned at once.</summary>
  [Range(1, 100)]
  public int MaxActiveRequestsPerUser { get; init; } = 3;

  /// <summary>Most requests across all users that can be queued, applying or provisioned at once.</summary>
  [Range(1, 1000)]
  public int MaxActiveRequestsTotal { get; init; } = 10;

  /// <summary>Chat messages one user can send per minute.</summary>
  [Range(1, 1000)]
  public int ChatMessagesPerMinute { get; init; } = 10;
}
