namespace ResourceProvisioner.Api.Tests;

using System.Text.Json.Nodes;
using Models;
using Services.Provisioning;
using Xunit;

public sealed class TerraformWorkspaceTests : IDisposable
{
  private readonly string _root = Path.Combine(Path.GetTempPath(), $"rp-ws-test-{Guid.NewGuid():N}");
  private readonly string _module = Path.Combine(Path.GetTempPath(), $"rp-module-test-{Guid.NewGuid():N}");

  public TerraformWorkspaceTests()
  {
    Directory.CreateDirectory(Path.Combine(_module, ".terraform"));
    Directory.CreateDirectory(Path.Combine(_module, "files"));
    foreach (var file in new[]
             {
               "main.tf", "variables.tf", "outputs.tf", "resource.json", ".terraform.lock.hcl", "terraform.tfstate",
               "terraform.tfvars.json", "old.tfplan", ".terraform/provider", "files/cloud-init.yaml",
             })
    {
      File.WriteAllText(Path.Combine(_module, file), file);
    }
  }

  [Fact]
  public void Prepare_CopiesModuleFiles_LeavingOutManifestStateAndTfvars()
  {
    var workspace = TerraformWorkspace.Prepare(_root, _module, Request("""{"name":"web"}"""));

    var files = Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
      .Select(f => Path.GetRelativePath(workspace, f).Replace('\\', '/'))
      .Order()
      .ToList();
    Assert.Equal(
      [
        ".terraform.lock.hcl", "files/cloud-init.yaml", "main.tf", "outputs.tf", "rp_backend.tf",
        "terraform.tfvars.json", "variables.tf",
      ],
      files);
  }

  [Fact]
  public void Prepare_LaysRunnerVariablesOverRequestVariables()
  {
    var request = Request("""{"name":"web","rp_tags":["sneaky"],"rp_name_suffix":"x"}""");

    var workspace = TerraformWorkspace.Prepare(_root, _module, request);

    var tfvars = JsonNode.Parse(File.ReadAllText(Path.Combine(workspace, TerraformWorkspace.VariablesFile)))!;
    Assert.Equal("web", tfvars["name"]!.GetValue<string>());
    Assert.Equal(request.Id.ToString("N")[..8], tfvars["rp_name_suffix"]!.GetValue<string>());
    Assert.Equal(
      ["resource-provisioner", $"rp-{request.Id}", "rp-owner-abc123"],
      tfvars["rp_tags"]!.AsArray().Select(t => t!.GetValue<string>()));
  }

  [Fact]
  public void Prepare_WritesPgBackendFile()
  {
    var workspace = TerraformWorkspace.Prepare(_root, _module, Request("{}"));

    var backend = File.ReadAllText(Path.Combine(workspace, TerraformWorkspace.BackendFile));
    Assert.Equal("terraform {\n  backend \"pg\" {}\n}\n", backend.ReplaceLineEndings("\n"));
  }

  public void Dispose()
  {
    foreach (var dir in new[] { _root, _module })
    {
      if (Directory.Exists(dir))
      {
        Directory.Delete(dir, recursive: true);
      }
    }
  }

  private static ProvisioningRequest Request(string variables) =>
    new() { Id = Guid.NewGuid(), OwnerId = "abc-123", Type = "sample", Variables = variables };
}
