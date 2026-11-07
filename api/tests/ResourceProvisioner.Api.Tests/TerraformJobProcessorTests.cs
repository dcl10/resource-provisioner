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
  [InlineData(RequestStatus.Cancelled)]
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
