namespace ResourceProvisioner.Api.Services.Provisioning;

using System.Text.Json;
using System.Text.Json.Nodes;
using Models;
using Requests;

/// <summary>
/// A request's working directory, <c>&lt;Workspaces:Root&gt;/&lt;requestId&gt;/</c>: a copy of its catalog module, its
/// <c>terraform.tfvars.json</c> and <see cref="BackendFile"/>. The state isn't here: the <c>pg</c> backend keeps it in
/// the app database (<see cref="TerraformStateStore"/>).
/// </summary>
public static class TerraformWorkspace
{
  /// <summary>The variables file the runner writes; Terraform loads it automatically.</summary>
  public const string VariablesFile = "terraform.tfvars.json";

  /// <summary>The saved plan that apply runs.</summary>
  public const string PlanFile = "plan.tfplan";

  /// <summary>
  /// The file the runner adds to every workspace to select the <c>pg</c> backend. Its settings come from the
  /// environment (<see cref="ITerraformStateStore.BackendEnvironment"/>), so the file holds no credentials. Catalog
  /// modules may not declare a backend of their own, or use this file name.
  /// </summary>
  public const string BackendFile = "rp_backend.tf";

  private const string BackendConfig = """
    terraform {
      backend "pg" {}
    }

    """;

  private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

  /// <summary>The workspace folder of a request.</summary>
  /// <param name="workspacesRoot"><c>Workspaces:Root</c>; a relative path resolves from the working directory.</param>
  /// <param name="requestId">Request ID.</param>
  /// <returns>The folder's full path.</returns>
  public static string PathFor(string workspacesRoot, Guid requestId) =>
    Path.Combine(Path.GetFullPath(workspacesRoot), requestId.ToString());

  /// <summary>
  /// Creates a request's workspace: copies the module (leaving out the manifest, any <c>.terraform/</c> folder, state,
  /// plans and tfvars), writes <see cref="VariablesFile"/>, the request's variables with the runner's forced
  /// variables (<see cref="RunnerVariables"/>) laid over them, and writes <see cref="BackendFile"/>.
  /// </summary>
  /// <param name="workspacesRoot"><c>Workspaces:Root</c>.</param>
  /// <param name="moduleDirectory">The type's catalog folder.</param>
  /// <param name="request">The request.</param>
  /// <param name="dependencyVariables">
  /// Runner variables taken from the requests it depends on (<c>RunnerVariables.ForDependencies</c>); laid over too.
  /// </param>
  /// <returns>The workspace's full path.</returns>
  public static string Prepare(
    string workspacesRoot,
    string moduleDirectory,
    ProvisioningRequest request,
    JsonObject? dependencyVariables = null)
  {
    var workspace = PathFor(workspacesRoot, request.Id);
    CopyModule(moduleDirectory, workspace);

    var variables = JsonNode.Parse(request.Variables)?.AsObject() ?? [];
    foreach (var (name, value) in RunnerVariables.For(request).Concat(dependencyVariables ?? []))
    {
      variables[name] = value?.DeepClone();
    }

    File.WriteAllText(Path.Combine(workspace, VariablesFile), variables.ToJsonString(Indented));
    File.WriteAllText(Path.Combine(workspace, BackendFile), BackendConfig);
    return workspace;
  }

  private static void CopyModule(string source, string target)
  {
    Directory.CreateDirectory(target);
    foreach (var file in Directory.EnumerateFiles(source))
    {
      var name = Path.GetFileName(file);
      if (!IsLeftOut(name))
      {
        File.Copy(file, Path.Combine(target, name), overwrite: true);
      }
    }

    foreach (var dir in Directory.EnumerateDirectories(source))
    {
      var name = Path.GetFileName(dir);
      if (name != ".terraform")
      {
        CopyModule(dir, Path.Combine(target, name));
      }
    }
  }

  private static bool IsLeftOut(string fileName) =>
    fileName == "resource.json" ||
    fileName.EndsWith(".tfplan", StringComparison.Ordinal) ||
    fileName.Contains(".tfstate", StringComparison.Ordinal) ||
    fileName.StartsWith("terraform.tfvars", StringComparison.Ordinal) ||
    fileName.EndsWith(".auto.tfvars", StringComparison.Ordinal) ||
    fileName.EndsWith(".auto.tfvars.json", StringComparison.Ordinal);
}
