namespace ResourceProvisioner.Api.Tests;

using System.Text.Json.Nodes;
using Data;
using Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Models;
using Services.Provisioning;
using Xunit;

/// <summary>The job processor against the fake Terraform CLI and a temp workspaces folder.</summary>
public class TerraformJobProcessorTests : IClassFixture<ApiFactory>
{
  private const string OwnerId = "3f2b8c1e-5d4a-4c1b-9a8e-0123456789ab";

  private readonly ApiFactory _factory;

  public TerraformJobProcessorTests(ApiFactory factory)
  {
    _factory = factory;
    _factory.Terraform.Reset();
    _factory.TerraformState.Reset();
  }

  [Fact]
  public async Task RunAsync_AppliesAndStoresOutputs_WhenEveryStepSucceeds()
  {
    var request = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Queued);

    await Run(request.Id);

    var stored = await Find(request.Id);
    Assert.Equal(RequestStatus.Succeeded, stored.Status);
    Assert.True(JsonNode.DeepEquals(
      JsonNode.Parse("""{ "id": "123", "ipv4_address": "192.0.2.1" }"""),
      JsonNode.Parse(stored.Outputs!)));
    Assert.True(JsonNode.DeepEquals(
      JsonNode.Parse("""{ "root_password": "output-secret" }"""),
      JsonNode.Parse(stored.SensitiveOutputs!.Values)));
  }

  [Fact]
  public async Task RunAsync_RunsInitPlanApplyOutput_InTheWorkspace()
  {
    var request = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Queued);

    await Run(request.Id);

    var invocations = _factory.Terraform.Invocations.ToList();
    Assert.Equal(
      [
        "init -input=false -no-color",
        $"workspace select -or-create=true -no-color {request.Id}",
        "plan -input=false -no-color -out=plan.tfplan",
        "apply -input=false -no-color plan.tfplan",
        "output -json -no-color",
      ],
      invocations.Select(i => string.Join(' ', i.Arguments)));
    var workspace = Path.Combine(_factory.WorkspacesRoot, request.Id.ToString());
    Assert.All(invocations, i => Assert.Equal(workspace, i.WorkingDirectory));
  }

  [Fact]
  public async Task RunAsync_PassesCredentialsInEnvironment_NotInTfvars()
  {
    var request = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Queued);

    await Run(request.Id);

    Assert.All(_factory.Terraform.Invocations, i =>
    {
      Assert.Equal("test-do-token", i.Environment["DIGITALOCEAN_TOKEN"]);
      Assert.Equal("1", i.Environment["TF_IN_AUTOMATION"]);
    });
    var tfvars = await File.ReadAllTextAsync(
      Path.Combine(_factory.WorkspacesRoot, request.Id.ToString(), TerraformWorkspace.VariablesFile));
    Assert.DoesNotContain("test-do-token", tfvars);
  }

  [Fact]
  public async Task RunAsync_PassesStateBackendInEnvironment_NotInFilesOrLog()
  {
    var request = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Queued);

    await Run(request.Id);

    Assert.All(_factory.Terraform.Invocations, i =>
    {
      Assert.Equal(FakeTerraformState.ConnectionString, i.Environment["PG_CONN_STR"]);
      Assert.Equal("terraform_state", i.Environment["PG_SCHEMA_NAME"]);
    });
    var workspace = Path.Combine(_factory.WorkspacesRoot, request.Id.ToString());
    Assert.All(
      Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories),
      f => Assert.DoesNotContain(FakeTerraformState.Password, File.ReadAllText(f)));
    Assert.All(await Lines(request.Id), l => Assert.DoesNotContain(FakeTerraformState.Password, l.Text));
  }

  [Fact]
  public async Task RunAsync_FailsWithoutRunningTerraform_WhenStateAlreadyExists()
  {
    var request = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Queued);
    _factory.TerraformState.Add(request.Id);

    await Run(request.Id);

    Assert.Equal(RequestStatus.Failed, (await Find(request.Id)).Status);
    Assert.Empty(_factory.Terraform.Invocations);
    Assert.Equal(
      $"Failed: Terraform already has state for request {request.Id}, so it wasn't applied again.",
      (await Lines(request.Id))[^1].Text);
  }

  [Fact]
  public async Task RunAsync_SavesTfvarsOnRequest_BeforeTerraformRuns()
  {
    var request = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Queued);
    _factory.Terraform.Script("plan", 1);

    await Run(request.Id);

    var stored = await Find(request.Id);
    Assert.Equal(RequestStatus.Failed, stored.Status);
    var tfvars = await File.ReadAllTextAsync(
      Path.Combine(_factory.WorkspacesRoot, request.Id.ToString(), TerraformWorkspace.VariablesFile));
    Assert.Equal(tfvars, stored.TerraformVariables);
    Assert.Contains($"rp-{request.Id}", stored.TerraformVariables, StringComparison.Ordinal);
  }

  [Fact]
  public async Task RunAsync_WritesTfvarsWithForcedTags()
  {
    var request = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Queued);

    await Run(request.Id);

    var tfvars = JsonNode.Parse(await File.ReadAllTextAsync(
      Path.Combine(_factory.WorkspacesRoot, request.Id.ToString(), TerraformWorkspace.VariablesFile)))!;
    Assert.Equal("web-1", tfvars["name"]!.GetValue<string>());
    Assert.Equal(request.Id.ToString("N")[..8], tfvars["rp_name_suffix"]!.GetValue<string>());
    Assert.Equal(
      ["resource-provisioner", $"rp-{request.Id}", "rp-owner-3f2b8c1e"],
      tfvars["rp_tags"]!.AsArray().Select(t => t!.GetValue<string>()));
  }

  [Fact]
  public async Task RunAsync_SavesLogLines_WithoutOutputValues()
  {
    var request = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Queued);
    _factory.Terraform.Script("apply", 0, (LogSource.Stdout, "Creating..."), (LogSource.Stderr, "a warning"));

    await Run(request.Id);

    var lines = await Lines(request.Id);
    Assert.Contains(lines, l => l is { Stream: LogSource.System, Text: "$ terraform init -input=false -no-color" });
    Assert.Contains(lines, l => l is { Stream: LogSource.Stdout, Text: "Creating..." });
    Assert.Contains(lines, l => l is { Stream: LogSource.Stderr, Text: "a warning" });
    Assert.DoesNotContain(lines, l => l.Text.Contains("output-secret", StringComparison.Ordinal));
    Assert.DoesNotContain(lines, l => l.Text.Contains("192.0.2.1", StringComparison.Ordinal));
    Assert.Equal(lines.OrderBy(l => l.Id).Select(l => l.Id), lines.Select(l => l.Id));
  }

  [Fact]
  public async Task RunAsync_FailsWithoutApplying_WhenPlanFails()
  {
    var request = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Queued);
    _factory.Terraform.Script("plan", 1, (LogSource.Stderr, "Error: invalid size"));

    await Run(request.Id);

    var stored = await Find(request.Id);
    Assert.Equal(RequestStatus.Failed, stored.Status);
    Assert.Null(stored.Outputs);
    Assert.DoesNotContain(_factory.Terraform.Invocations, i => i.Arguments[0] == "apply");
    var lines = await Lines(request.Id);
    Assert.Contains(lines, l => l.Text == "Error: invalid size");
    Assert.Equal("Failed: terraform plan exited with code 1.", lines[^1].Text);
  }

  [Fact]
  public async Task RunAsync_Fails_WhenApplyFails()
  {
    var request = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Queued);
    _factory.Terraform.Script("apply", 1);

    await Run(request.Id);

    Assert.Equal(RequestStatus.Failed, (await Find(request.Id)).Status);
    Assert.DoesNotContain(_factory.Terraform.Invocations, i => i.Arguments[0] == "output");
  }

  [Fact]
  public async Task RunAsync_FailsWithoutLeakingOutput_WhenOutputIsNotJson()
  {
    var request = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Queued);
    _factory.Terraform.Script("output", 0, (LogSource.Stdout, "not json output-secret"));

    await Run(request.Id);

    Assert.Equal(RequestStatus.Failed, (await Find(request.Id)).Status);
    Assert.DoesNotContain(await Lines(request.Id), l => l.Text.Contains("output-secret", StringComparison.Ordinal));
  }

  [Theory]
  [InlineData(RequestStatus.AwaitingConfirmation)]
  [InlineData(RequestStatus.Succeeded)]
  [InlineData(RequestStatus.Failed)]
  public async Task RunAsync_DoesNothing_WhenNotQueued(RequestStatus status)
  {
    var request = await _factory.SeedRequestAsync(OwnerId, status);

    await Run(request.Id);

    Assert.Equal(status, (await Find(request.Id)).Status);
    Assert.Empty(_factory.Terraform.Invocations);
  }

  [Fact]
  public async Task RunAsync_DoesNothing_WhenRequestDoesNotExist()
  {
    await Run(Guid.NewGuid());

    Assert.Empty(_factory.Terraform.Invocations);
  }

  [Fact]
  public async Task RunAsync_Fails_WhenTypeIsNoLongerInCatalog()
  {
    var request = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Queued, type: "retired-type");

    await Run(request.Id);

    Assert.Equal(RequestStatus.Failed, (await Find(request.Id)).Status);
    Assert.Empty(_factory.Terraform.Invocations);
    Assert.Equal(
      "Failed: Resource type 'retired-type' is no longer in the catalog.",
      (await Lines(request.Id))[^1].Text);
  }

  [Fact]
  public async Task RunAsync_DestroysThenDeletesTheRequestAndEverythingStoredForIt_WhenDestroying()
  {
    var request = await SeedDestroying(withState: true);
    var dependency = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Succeeded);
    await _factory.AddDependencyAsync(request.Id, dependency.Id);

    await Run(request.Id);

    Assert.Null(await _factory.FindRequestAsync(request.Id));
    using (var scope = _factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      Assert.False(await db.RequestSensitiveOutputs.AnyAsync(x => x.RequestId == request.Id));
      Assert.False(await db.JobLogLines.AnyAsync(x => x.RequestId == request.Id));
      Assert.False(await db.RequestDependencies.AnyAsync(x => x.RequestId == request.Id));
    }

    Assert.Contains(request.Id, _factory.TerraformState.Deleted);
    Assert.False(await _factory.TerraformState.HasStateAsync(request.Id, CancellationToken.None));
    Assert.NotNull(await _factory.FindRequestAsync(dependency.Id));
    var workspace = Path.Combine(_factory.WorkspacesRoot, request.Id.ToString());
    Assert.Equal(
      [
        "init -input=false -no-color",
        $"workspace select -no-color {request.Id}",
        "destroy -auto-approve -input=false -no-color",
      ],
      _factory.Terraform.Invocations.Select(i => string.Join(' ', i.Arguments)));
    Assert.All(_factory.Terraform.Invocations, i =>
    {
      Assert.Equal(workspace, i.WorkingDirectory);
      Assert.Equal("test-do-token", i.Environment["DIGITALOCEAN_TOKEN"]);
    });
  }

  [Fact]
  public async Task RunAsync_DeletesTheProjectAndUnlinksItsProposals_WhenDestroyingAProjectsRequest()
  {
    var project = await _factory.SeedProjectAsync(OwnerId, RequestStatus.Destroying);
    var proposal = await _factory.SeedRequestAsync(OwnerId, RequestStatus.AwaitingConfirmation, projectId: project.Id);
    await _factory.AddDependencyAsync(proposal.Id, project.ProjectRequestId);

    await Run(project.ProjectRequestId);

    Assert.Null(await _factory.FindRequestAsync(project.ProjectRequestId));
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    Assert.False(await db.Projects.AnyAsync(p => p.Id == project.Id));
    Assert.False(await db.RequestDependencies.AnyAsync(d => d.DependsOnId == project.ProjectRequestId));
    var kept = await db.ProvisioningRequests.SingleAsync(x => x.Id == proposal.Id);
    Assert.Equal((RequestStatus.AwaitingConfirmation, (Guid?)null), (kept.Status, kept.ProjectId));
  }

  [Fact]
  public async Task RunAsync_DeletesWithoutRunningTerraform_WhenThereIsNoState()
  {
    var request = await SeedDestroying(withState: false);

    await Run(request.Id);

    Assert.Null(await _factory.FindRequestAsync(request.Id));
    Assert.Empty(_factory.Terraform.Invocations);
  }

  [Fact]
  public async Task RunAsync_FailsAndKeepsEverything_WhenDestroyFails()
  {
    var request = await SeedDestroying(withState: true);
    _factory.Terraform.Script("destroy", 1, (LogSource.Stderr, "Error: droplet is locked"));

    await Run(request.Id);

    var stored = await Find(request.Id);
    Assert.Equal(RequestStatus.Failed, stored.Status);
    Assert.NotNull(stored.Outputs);
    Assert.NotNull(stored.SensitiveOutputs);
    Assert.DoesNotContain(request.Id, _factory.TerraformState.Deleted);
    Assert.True(await _factory.TerraformState.HasStateAsync(request.Id, CancellationToken.None));
    var lines = await Lines(request.Id);
    Assert.Contains(lines, l => l.Text == "Error: droplet is locked");
    Assert.Equal("Failed: terraform destroy exited with code 1.", lines[^1].Text);
  }

  [Fact]
  public async Task RunAsync_FailsAndKeepsEverything_WhenSomethingThatMayExistStillDependsOnIt()
  {
    // Destroy refuses this; the delete checks again rather than orphan a live dependent.
    var request = await SeedDestroying(withState: true);
    var dependent = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Succeeded);
    await _factory.AddDependencyAsync(dependent.Id, request.Id);

    await Run(request.Id);

    var stored = await Find(request.Id);
    Assert.Equal(RequestStatus.Failed, stored.Status);
    Assert.NotNull(stored.SensitiveOutputs);
    Assert.DoesNotContain(request.Id, _factory.TerraformState.Deleted);
    Assert.NotNull(await _factory.FindRequestAsync(dependent.Id));
  }

  [Fact]
  public async Task RunAsync_FailsAndKeepsOutputs_WhenDestroyingTypeIsNoLongerInCatalog()
  {
    var request = await SeedDestroying(withState: true, type: "retired-type");

    await Run(request.Id);

    var stored = await Find(request.Id);
    Assert.Equal(RequestStatus.Failed, stored.Status);
    Assert.NotNull(stored.Outputs);
    Assert.Empty(_factory.Terraform.Invocations);
    Assert.Equal(
      "Failed: Resource type 'retired-type' is no longer in the catalog, so Terraform can't destroy it; an admin " +
      $"can delete its resources in DigitalOcean by the tag rp-{request.Id}.",
      (await Lines(request.Id))[^1].Text);
  }

  [Fact]
  public async Task RunAsync_FailsDestroy_WhenTfvarsWereNotSaved()
  {
    var request = await SeedDestroying(withState: true, terraformVariables: null);

    await Run(request.Id);

    Assert.Equal(RequestStatus.Failed, (await Find(request.Id)).Status);
    Assert.Empty(_factory.Terraform.Invocations);
    Assert.StartsWith(
      "Failed: The variables its apply used weren't saved",
      (await Lines(request.Id))[^1].Text,
      StringComparison.Ordinal);
  }

  [Fact]
  public async Task RunAsync_DestroysInRebuiltWorkspace_WithSavedTfvars()
  {
    const string saved = """{ "name": "saved-at-apply" }""";
    var request = await SeedDestroying(withState: true, terraformVariables: saved);

    await Run(request.Id);

    Assert.Null(await _factory.FindRequestAsync(request.Id));
    var workspace = Path.Combine(_factory.WorkspacesRoot, request.Id.ToString());
    Assert.Equal(saved, await File.ReadAllTextAsync(Path.Combine(workspace, TerraformWorkspace.VariablesFile)));
    Assert.True(File.Exists(Path.Combine(workspace, "main.tf")));
    Assert.True(File.Exists(Path.Combine(workspace, TerraformWorkspace.BackendFile)));
  }

  [Fact]
  public async Task RunAsync_DestroysWhatItApplied()
  {
    var request = await _factory.SeedRequestAsync(OwnerId, RequestStatus.Queued);
    await Run(request.Id);

    // The fake doesn't write state; a real apply would. Nothing on disk needs to survive between jobs.
    _factory.TerraformState.Add(request.Id);
    Directory.Delete(_factory.WorkspacesRoot, recursive: true);
    await SetStatus(request.Id, RequestStatus.Destroying);
    var applied = (await Find(request.Id)).TerraformVariables;
    await Run(request.Id);

    Assert.Null(await _factory.FindRequestAsync(request.Id));
    Assert.Single(
      _factory.Terraform.Invocations.Select(i => i.WorkingDirectory).Distinct());
    Assert.Equal(
      applied,
      await File.ReadAllTextAsync(
        Path.Combine(_factory.WorkspacesRoot, request.Id.ToString(), TerraformWorkspace.VariablesFile)));
  }

  private async Task<ProvisioningRequest> SeedDestroying(
    bool withState,
    string type = "test-server",
    string? terraformVariables = RequestSeeding.ServerVariables)
  {
    var request = await _factory.SeedRequestAsync(
      OwnerId,
      RequestStatus.Destroying,
      type,
      outputs: """{"id":"123"}""",
      sensitiveOutputs: """{"root_password":"output-secret"}""",
      terraformVariables: terraformVariables);
    if (withState)
    {
      _factory.TerraformState.Add(request.Id);
    }

    return request;
  }

  private async Task SetStatus(Guid id, RequestStatus status)
  {
    using var scope = _factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    (await db.ProvisioningRequests.SingleAsync(x => x.Id == id)).Status = status;
    await db.SaveChangesAsync();
  }

  private async Task Run(Guid id)
  {
    using var scope = _factory.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<ITerraformJobProcessor>().RunAsync(id, CancellationToken.None);
  }

  private async Task<ProvisioningRequest> Find(Guid id)
  {
    using var scope = _factory.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<AppDbContext>().ProvisioningRequests
      .Include(x => x.SensitiveOutputs)
      .SingleAsync(x => x.Id == id);
  }

  private async Task<List<JobLogLine>> Lines(Guid id)
  {
    using var scope = _factory.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<AppDbContext>().JobLogLines
      .Where(x => x.RequestId == id)
      .OrderBy(x => x.Id)
      .ToListAsync();
  }
}
