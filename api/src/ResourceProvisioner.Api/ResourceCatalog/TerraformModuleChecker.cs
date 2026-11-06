namespace ResourceProvisioner.Api.ResourceCatalog;

using System.Text.RegularExpressions;

/// <summary>
/// Checks that a manifest matches its Terraform module: the module files exist, every manifest variable is declared in
/// <c>variables.tf</c>, the outputs match <c>outputs.tf</c> exactly, and <c>sensitive</c> agrees on both sides. This is
/// a light read of <c>terraform fmt</c>-formatted files (top-level blocks close with <c>}</c> in column 0), not an HCL
/// parser.
/// </summary>
internal static partial class TerraformModuleChecker
{
  /// <summary>The files every module must have.</summary>
  public static readonly string[] RequiredFiles = ["main.tf", "variables.tf", "outputs.tf"];

  /// <summary>Returns every mismatch between <paramref name="manifest"/> and the module in its folder.</summary>
  public static List<string> Check(ResourceManifest manifest, string moduleDirectory)
  {
    var problems = RequiredFiles
      .Where(f => !File.Exists(Path.Combine(moduleDirectory, f)))
      .Select(f => $"{f} is missing.")
      .ToList();
    if (problems.Count > 0)
    {
      return problems;
    }

    // Variables not in the manifest are allowed: the runner sets some itself (tags, for example).
    var variables = ReadBlocks(Path.Combine(moduleDirectory, "variables.tf"), "variable");
    foreach (var variable in manifest.Variables)
    {
      if (!variables.TryGetValue(variable.Name, out var sensitive))
      {
        problems.Add($"variables.{variable.Name} is not declared in variables.tf.");
      }
      else if (sensitive != variable.Sensitive)
      {
        problems.Add($"variables.{variable.Name}: sensitive is {Bool(variable.Sensitive)} in resource.json but " +
                     $"{Bool(sensitive)} in variables.tf.");
      }
    }

    // Every output must be listed, so nothing secret can slip past the manifest's sensitive flags.
    var outputs = ReadBlocks(Path.Combine(moduleDirectory, "outputs.tf"), "output");
    foreach (var output in manifest.Outputs)
    {
      if (!outputs.TryGetValue(output.Name, out var sensitive))
      {
        problems.Add($"outputs.{output.Name} is not declared in outputs.tf.");
      }
      else if (sensitive != output.Sensitive)
      {
        problems.Add($"outputs.{output.Name}: sensitive is {Bool(output.Sensitive)} in resource.json but " +
                     $"{Bool(sensitive)} in outputs.tf.");
      }
    }

    problems.AddRange(outputs.Keys
      .Where(name => manifest.Outputs.All(o => o.Name != name))
      .Select(name => $"output '{name}' in outputs.tf is missing from resource.json."));

    return problems;
  }

  /// <summary>Reads the top-level blocks of one kind: name to whether the block sets <c>sensitive = true</c>.</summary>
  private static Dictionary<string, bool> ReadBlocks(string path, string kind) =>
    BlockRegex().Matches(File.ReadAllText(path))
      .Where(m => m.Groups["kind"].Value == kind)
      .GroupBy(m => m.Groups["name"].Value)
      .ToDictionary(g => g.Key, g => g.Any(m => SensitiveRegex().IsMatch(m.Groups["body"].Value)));

  private static string Bool(bool value) => value ? "true" : "false";

  [GeneratedRegex("""^(?<kind>variable|output)\s+"(?<name>[^"]+)"\s*\{(?<body>.*?)^\}""",
    RegexOptions.Multiline | RegexOptions.Singleline)]
  private static partial Regex BlockRegex();

  // Only the block's own attribute (indented two spaces by terraform fmt), not one inside a nested block.
  [GeneratedRegex(@"^  sensitive\s*=\s*true\s*$", RegexOptions.Multiline)]
  private static partial Regex SensitiveRegex();
}
