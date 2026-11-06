namespace ResourceProvisioner.Api.ResourceCatalog;

using System.Text.Json.Nodes;

/// <summary>The catalog held in memory. Build it with <see cref="CatalogLoader.Load"/>.</summary>
/// <param name="entries">The loaded resource types.</param>
public class CatalogService(IReadOnlyList<CatalogEntry> entries) : ICatalogService
{
  /// <summary>The field path an unknown resource type is reported on.</summary>
  public const string TypeErrorKey = "type";

  private readonly Dictionary<string, CatalogEntry> _byId = entries.ToDictionary(e => e.Manifest.Id);

  /// <inheritdoc />
  public IReadOnlyList<ResourceManifest> Types { get; } = [.. entries.Select(e => e.Manifest).OrderBy(m => m.Id)];

  /// <inheritdoc />
  public ResourceManifest? Find(string type) => _byId.GetValueOrDefault(type)?.Manifest;

  /// <inheritdoc />
  public string GetModuleDirectory(string type) =>
    _byId.TryGetValue(type, out var entry)
      ? entry.ModuleDirectory
      : throw new KeyNotFoundException($"Unknown resource type '{type}'.");

  /// <inheritdoc />
  public VariableValidationResult ValidateVariables(string type, JsonObject? values)
  {
    var errors = new ValidationErrors();
    if (Find(type) is not { } manifest)
    {
      errors.Add(TypeErrorKey, $"Unknown resource type '{type}'.");
      return new VariableValidationResult([], errors.ToDictionary());
    }

    var normalized = VariableValidator.ValidateObject(manifest.Variables, values, "", errors);
    VariableValidator.CheckAtLeastOneOf(manifest.AtLeastOneOf ?? [], normalized, errors);
    return new VariableValidationResult(normalized, errors.ToDictionary());
  }
}
