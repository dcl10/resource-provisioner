namespace ResourceProvisioner.Api.ResourceCatalog;

using System.Text.Json.Serialization;

/// <summary>
/// A resource type's <c>resource.json</c>: the single source of truth for the agent's tool schemas, server-side
/// validation and the confirmation summary.
/// </summary>
public record ResourceManifest
{
  /// <summary>The type's ID, in <c>kebab-case</c>; the same as its folder name under <c>catalog/</c>.</summary>
  public required string Id { get; init; }

  /// <summary>A human name, such as "Droplet".</summary>
  public required string DisplayName { get; init; }

  /// <summary>What the resource is and when to pick it, for the agent and the user.</summary>
  public required string Description { get; init; }

  /// <summary>A rough cost, such as "About $4 to $6 a month", shown before the user confirms.</summary>
  public required string CostHint { get; init; }

  /// <summary>The variables a request can set, each matching a <c>variable</c> in <c>variables.tf</c>.</summary>
  public IReadOnlyList<VariableSpec> Variables { get; init; } = [];

  /// <summary>Every output in <c>outputs.tf</c>, with which ones are secrets.</summary>
  public IReadOnlyList<OutputSpec> Outputs { get; init; } = [];

  /// <summary>
  /// Rules across variables: each entry names two or more top-level variables, at least one of which a request must
  /// set (not null, an empty string or an empty array, after defaults). Null when the type has none.
  /// </summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public IReadOnlyList<IReadOnlyList<string>>? AtLeastOneOf { get; init; }
}
