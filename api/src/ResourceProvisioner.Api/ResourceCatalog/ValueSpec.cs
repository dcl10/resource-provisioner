namespace ResourceProvisioner.Api.ResourceCatalog;

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>
/// The shape and constraints of a value: a variable, an array's elements or an object's field. Each keyword matches
/// the JSON Schema keyword of the same name (<c>allowedValues</c> is <c>enum</c>); see <c>docs/catalog.md</c>.
/// </summary>
public record ValueSpec
{
  /// <summary>The value's type.</summary>
  public required VariableType Type { get; init; }

  /// <summary>What the value is, written for the agent and the user. Required on variables and object fields.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Description { get; init; }

  /// <summary>The only values accepted (JSON Schema <c>enum</c>). Strings and numbers only.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public IReadOnlyList<JsonNode>? AllowedValues { get; init; }

  /// <summary>A regular expression a string must match. Anchor it (<c>^...$</c>) to match the whole value.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Pattern { get; init; }

  /// <summary>Smallest allowed number, inclusive.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public decimal? Minimum { get; init; }

  /// <summary>Largest allowed number, inclusive.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public decimal? Maximum { get; init; }

  /// <summary>Shortest allowed string.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MinLength { get; init; }

  /// <summary>Longest allowed string.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MaxLength { get; init; }

  /// <summary>Fewest allowed array elements.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MinItems { get; init; }

  /// <summary>Most allowed array elements.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public int? MaxItems { get; init; }

  /// <summary>The spec every element of an array must match. Required for arrays.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public ValueSpec? Items { get; init; }

  /// <summary>An object's fields. Required for objects; no other fields are accepted.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public IReadOnlyList<VariableSpec>? Properties { get; init; }
}
