namespace ResourceProvisioner.Api.ResourceCatalog;

using Services.Requests;

using System.Text.Json;

/// <summary>Reads every <c>&lt;type&gt;/resource.json</c> under the catalog folder and checks it.</summary>
public static class CatalogLoader
{
  /// <summary>The manifest's file name inside each type's folder.</summary>
  public const string ManifestFileName = "resource.json";

  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    // A misspelt keyword ("allowedValue") would otherwise be ignored silently and its constraint lost.
    UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    RespectNullableAnnotations = true,
    PropertyNameCaseInsensitive = false,
  };

  /// <summary>
  /// Resolves <c>Catalog:Root</c>. An absolute path is returned as-is. A relative one is looked for in
  /// <paramref name="startDirectory"/> and then each folder above it; the first that exists wins. If none does, the
  /// path under <paramref name="startDirectory"/> is returned, so <see cref="Load"/> reports it.
  /// </summary>
  /// <param name="root">The configured catalog folder.</param>
  /// <param name="startDirectory">Where to start looking, normally the content root.</param>
  /// <returns>A full path.</returns>
  public static string ResolveRoot(string root, string startDirectory)
  {
    if (Path.IsPathRooted(root))
    {
      return Path.GetFullPath(root);
    }

    for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
    {
      var candidate = Path.Combine(dir.FullName, root);
      if (Directory.Exists(candidate))
      {
        return Path.GetFullPath(candidate);
      }
    }

    return Path.GetFullPath(Path.Combine(startDirectory, root));
  }

  /// <summary>
  /// Loads the catalog. Every folder under <paramref name="root"/> (except hidden ones) is a resource type and must
  /// hold a valid <c>resource.json</c> that matches its module.
  /// </summary>
  /// <param name="root">The catalog folder, normally from <see cref="ResolveRoot"/>.</param>
  /// <returns>The resource types, ordered by ID.</returns>
  /// <exception cref="CatalogException">
  /// The folder doesn't exist, or any manifest is invalid. The message lists every problem in every manifest.
  /// </exception>
  public static IReadOnlyList<CatalogEntry> Load(string root)
  {
    var fullRoot = Path.GetFullPath(root);
    if (!Directory.Exists(fullRoot))
    {
      throw new CatalogException(
        $"Catalog folder '{fullRoot}' doesn't exist. Set Catalog:Root to the catalog/ folder's path.");
    }

    var entries = new List<CatalogEntry>();
    var problems = new List<string>();

    var folders = Directory.GetDirectories(fullRoot)
      .Where(d => !Path.GetFileName(d).StartsWith('.'))
      .Order(StringComparer.Ordinal);

    foreach (var folder in folders)
    {
      var manifestPath = Path.Combine(folder, ManifestFileName);
      var folderProblems = new List<string>();
      var manifest = Read(manifestPath, folderProblems);

      if (manifest is not null)
      {
        folderProblems.AddRange(ManifestChecker.Check(manifest, Path.GetFileName(folder)));
        folderProblems.AddRange(TerraformModuleChecker.Check(manifest, folder));
      }

      if (folderProblems.Count > 0)
      {
        problems.Add($"{manifestPath}:{Environment.NewLine}" +
                     string.Join(Environment.NewLine, folderProblems.Select(p => $"  - {p}")));
      }
      else
      {
        entries.Add(new CatalogEntry(manifest!, folder, TerraformModuleChecker.ReadVariableNames(folder)));
      }
    }

    // References point at other types, so they can only be checked once every manifest is read.
    var ids = entries.Select(e => e.Manifest.Id).ToHashSet(StringComparer.Ordinal);
    var byId = entries.ToDictionary(e => e.Manifest.Id, e => e.Manifest, StringComparer.Ordinal);
    foreach (var entry in entries)
    {
      foreach (var variable in entry.Manifest.Variables.Where(v =>
                 RunnerVariables.DependencyVariable(v) is not null &&
                 byId.TryGetValue(v.References!, out var target) &&
                 target.Outputs.All(o => o.Name != RunnerVariables.DependencyOutput)))
      {
        problems.Add($"{Path.Combine(entry.ModuleDirectory, ManifestFileName)}:{Environment.NewLine}" +
                     $"  - {variable.Name}: the runner reads the '{RunnerVariables.DependencyOutput}' output of " +
                     $"'{variable.References}', which has none.");
      }

      // rp_project_id comes from the request's project, so no reference may set it too.
      foreach (var variable in entry.Manifest.Variables.Where(v =>
                 RunnerVariables.DependencyVariable(v) == RunnerVariables.ProjectIdVariable))
      {
        problems.Add($"{Path.Combine(entry.ModuleDirectory, ManifestFileName)}:{Environment.NewLine}" +
                     $"  - {variable.Name}: the runner sets {RunnerVariables.ProjectIdVariable} from the request's " +
                     "project, so a reference can't set it.");
      }

      if (entry.ModuleVariables?.Contains(RunnerVariables.ProjectIdVariable) == true &&
          (!byId.TryGetValue(Models.Project.RequestType, out var project) ||
           project.Outputs.All(o => o.Name != RunnerVariables.DependencyOutput)))
      {
        problems.Add($"{Path.Combine(entry.ModuleDirectory, ManifestFileName)}:{Environment.NewLine}" +
                     $"  - variables.tf declares {RunnerVariables.ProjectIdVariable}, which the runner reads from " +
                     $"the '{RunnerVariables.DependencyOutput}' output of the '{Models.Project.RequestType}' type, " +
                     "but there's no such type or output.");
      }

      var unknown = entry.Manifest.Variables.Where(v => v.References is not null && !ids.Contains(v.References));
      foreach (var variable in unknown)
      {
        problems.Add($"{Path.Combine(entry.ModuleDirectory, ManifestFileName)}:{Environment.NewLine}" +
                     $"  - {variable.Name}: references '{variable.References}', which isn't a type in the catalog.");
      }
    }

    if (problems.Count > 0)
    {
      throw new CatalogException(
        $"Invalid catalog in '{fullRoot}':{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    return entries;
  }

  private static ResourceManifest? Read(string manifestPath, List<string> problems)
  {
    if (!File.Exists(manifestPath))
    {
      problems.Add($"{ManifestFileName} is missing.");
      return null;
    }

    try
    {
      var manifest = JsonSerializer.Deserialize<ResourceManifest>(File.ReadAllText(manifestPath), JsonOptions);
      if (manifest is null)
      {
        problems.Add($"{ManifestFileName} is empty.");
      }

      return manifest;
    }
    catch (JsonException e)
    {
      problems.Add($"{ManifestFileName} is not a valid manifest: {e.Message}");
      return null;
    }
  }
}
