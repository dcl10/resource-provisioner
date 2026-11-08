namespace ResourceProvisioner.Api.Services.Provisioning;

using System.Text.Json;
using System.Text.Json.Nodes;
using Models;
using Requests;

/// <summary>
/// A request's working directory, <c>&lt;Workspaces:Root&gt;/&lt;requestId&gt;/</c>: a copy of its catalog module, its
/// <c>terraform.tfvars.json</c> and <see cref="BackendFile"/>. Nothing in it needs keeping: the <c>pg</c> backend keeps
/// the state in the app database (<see cref="TerraformStateStore"/>), and the tfvars are saved on the request
/// (<see cref="ProvisioningRequest.TerraformVariables"/>), so every job prepares it afresh.
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
  /// The contents of a request's <see cref="VariablesFile"/>: its variables with the runner's forced variables
  /// (<see cref="RunnerVariables"/>) laid over them.
  /// </summary>
  /// <param name="request">The request.</param>
  /// <param name="dependencyVariables">
  /// Runner variables taken from the requests it depends on (<c>RunnerVariables.ForDependencies</c>); laid over too.
  /// </param>
  /// <returns>Indented JSON.</returns>
  public static string BuildVariables(ProvisioningRequest request, JsonObject? dependencyVariables = null)
  {
    var variables = JsonNode.Parse(request.Variables)?.AsObject() ?? [];
    foreach (var (name, value) in RunnerVariables.For(request).Concat(dependencyVariables ?? []))
    {
      variables[name] = value?.DeepClone();
    }

    return variables.ToJsonString(Indented);
  }

  /// <summary>
  /// Creates a request's workspace afresh, deleting whatever an earlier job left there: copies the module (leaving out
  /// the manifest, any <c>.terraform/</c> folder, state, plans and tfvars), then writes <see cref="VariablesFile"/>
  /// and <see cref="BackendFile"/>.
  /// </summary>
  /// <param name="workspacesRoot"><c>Workspaces:Root</c>.</param>
  /// <param name="moduleDirectory">The type's catalog folder.</param>
  /// <param name="requestId">The request ID.</param>
  /// <param name="variablesJson">The tfvars, from <see cref="BuildVariables"/>.</param>
  /// <returns>The workspace's full path.</returns>
  public static string Prepare(string workspacesRoot, string moduleDirectory, Guid requestId, string variablesJson)
  {
    var workspace = PathFor(workspacesRoot, requestId);
    if (Directory.Exists(workspace))
    {
      Directory.Delete(workspace, recursive: true);
    }

    CopyModule(moduleDirectory, workspace);
    File.WriteAllText(Path.Combine(workspace, VariablesFile), variablesJson);
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
