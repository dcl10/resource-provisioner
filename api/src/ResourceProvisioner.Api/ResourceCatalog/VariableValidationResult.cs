namespace ResourceProvisioner.Api.ResourceCatalog;

using System.Text.Json.Nodes;

/// <summary>The outcome of checking a request's variables against a resource type's manifest.</summary>
/// <param name="Values">
/// The variables with defaults applied, ready to become <c>terraform.tfvars.json</c>. Only meaningful when
/// <see cref="IsValid"/>.
/// </param>
/// <param name="Errors">
/// Problems keyed by field path (<c>size</c>, <c>tags[1]</c>, <c>rules[0].port</c>), in the shape of ASP.NET Core's
/// validation problem details. Empty when valid.
/// </param>
public record VariableValidationResult(JsonObject Values, IReadOnlyDictionary<string, string[]> Errors)
{
  /// <summary>Whether every variable is valid.</summary>
  public bool IsValid => Errors.Count == 0;
}
