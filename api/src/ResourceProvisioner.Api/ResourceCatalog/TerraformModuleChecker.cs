namespace ResourceProvisioner.Api.ResourceCatalog;

using System.Text.RegularExpressions;
using Services.Provisioning;
using Services.Requests;

/// <summary>
/// Checks that a manifest matches its Terraform module: the module files exist, every manifest variable is declared in
/// <c>variables.tf</c>, the outputs match <c>outputs.tf</c> exactly, <c>sensitive</c> agrees on both sides, and the
/// module leaves the state backend to the runner (no <c>backend</c> block, no <c>rp_backend.tf</c>). This is
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

    // The runner adds the pg backend to every workspace; a module's own backend would clash with it.
    if (File.Exists(Path.Combine(moduleDirectory, TerraformWorkspace.BackendFile)))
    {
      problems.Add($"{TerraformWorkspace.BackendFile} is reserved for the runner's state backend.");
    }

    problems.AddRange(Directory.EnumerateFiles(moduleDirectory, "*.tf")
      .Where(f => BackendRegex().IsMatch(File.ReadAllText(f)))
      .Select(f => $"{Path.GetFileName(f)} declares a backend; the runner sets the state backend itself."));

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

    // A single reference named <x>_request_id gets rp_<x>_id from the runner, so the module must take it.
    foreach (var reference in manifest.Variables.Where(v => RunnerVariables.DependencyVariable(v) is not null))
    {
      var runnerVariable = RunnerVariables.DependencyVariable(reference)!;
      if (!variables.ContainsKey(runnerVariable))
      {
        problems.Add($"variables.{reference.Name}: the runner sets {runnerVariable} from it, but variables.tf " +
                     "doesn't declare it.");
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

  [GeneratedRegex(@"^\s*backend\s+""", RegexOptions.Multiline)]
  private static partial Regex BackendRegex();

  // Only the block's own attribute (indented two spaces by terraform fmt), not one inside a nested block.
  [GeneratedRegex(@"^  sensitive\s*=\s*true\s*$", RegexOptions.Multiline)]
  private static partial Regex SensitiveRegex();
}
