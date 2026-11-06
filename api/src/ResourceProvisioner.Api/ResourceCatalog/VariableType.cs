namespace ResourceProvisioner.Api.ResourceCatalog;

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

/// <summary>
/// The type of a manifest variable. The names are JSON Schema's, so a variable maps straight onto a tool input schema.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<VariableType>))]
[SuppressMessage("Naming", "CA1720:Identifier contains type name",
  Justification = "The members are JSON Schema's type names on purpose.")]
public enum VariableType
{
  /// <summary>A string (Terraform <c>string</c>).</summary>
  [JsonStringEnumMemberName("string")]
  String,

  /// <summary>A whole number (Terraform <c>number</c>).</summary>
  [JsonStringEnumMemberName("integer")]
  Integer,

  /// <summary>Any number (Terraform <c>number</c>).</summary>
  [JsonStringEnumMemberName("number")]
  Number,

  /// <summary>True or false (Terraform <c>bool</c>).</summary>
  [JsonStringEnumMemberName("boolean")]
  Boolean,

  /// <summary>A list whose elements match <see cref="ValueSpec.Items"/> (Terraform <c>list(...)</c>).</summary>
  [JsonStringEnumMemberName("array")]
  Array,

  /// <summary>An object with the fields in <see cref="ValueSpec.Properties"/> (Terraform <c>object({...})</c>).</summary>
  [JsonStringEnumMemberName("object")]
  Object,
}
