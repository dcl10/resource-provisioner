namespace ResourceProvisioner.Api.ResourceCatalog;

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

/// <summary>
/// A named value: one of a resource type's variables (a Terraform <c>variable</c> of the same name), or a field of an
/// object-typed variable.
/// </summary>
public record VariableSpec : ValueSpec
{
  /// <summary>The Terraform variable or object field name, in <c>snake_case</c>.</summary>
  public required string Name { get; init; }

  /// <summary>A short human label for the confirmation summary. Falls back to <see cref="Name"/>.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public string? Label { get; init; }

  /// <summary>Whether a value must be given. A required variable can't have a <see cref="Default"/>.</summary>
  public bool Required { get; init; }

  /// <summary>The value used when none is given. Must itself satisfy the spec.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public JsonNode? Default { get; init; }

  /// <summary>
  /// Whether the value is a secret. Sensitive variables are left out of the agent's tool schemas and masked in
  /// summaries, must be marked <c>sensitive = true</c> in <c>variables.tf</c>, and can be neither required nor
  /// defaulted. Top-level variables only.
  /// </summary>
  public bool Sensitive { get; init; }
}
