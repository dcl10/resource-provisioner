namespace ResourceProvisioner.Api.Services.Provisioning;

using System.Text;
using Config;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Models;
using Requests;
using ResourceCatalog;
using Terraform;

/// <summary>
/// <see cref="ITerraformJobProcessor"/>. An apply prepares the request's workspace, then runs <c>terraform init</c>,
/// <c>plan -out=plan.tfplan</c>, <c>apply plan.tfplan</c> and <c>output -json</c>; a destroy runs <c>init</c> and
/// <c>destroy -auto-approve</c> in the same workspace. Every line of output is saved as a <see cref="JobLogLine"/>
/// except <c>output</c>'s, which holds secrets.
/// </summary>
/// <param name="db">The app database.</param>
/// <param name="catalog">The resource catalog.</param>
/// <param name="terraform">Runs the CLI.</param>
/// <param name="workspaces">Where workspaces go.</param>
/// <param name="digitalOcean">The DigitalOcean token, passed to Terraform in its environment.</param>
/// <param name="spaces">The Spaces keys, passed to Terraform in its environment.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">Logs what the user doesn't see.</param>
public partial class TerraformJobProcessor(
  AppDbContext db,
  ICatalogService catalog,
  ITerraformProcessRunner terraform,
  IOptions<WorkspacesOptions> workspaces,
  IOptions<DigitalOceanOptions> digitalOcean,
  IOptions<SpacesOptions> spaces,
  TimeProvider time,
  ILogger<TerraformJobProcessor> logger) : ITerraformJobProcessor
{
  /// <inheritdoc />
  public async Task RunAsync(Guid requestId, CancellationToken cancellationToken)
  {
    var request = await db.ProvisioningRequests.SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken);
    switch (request?.Status)
    {
      case RequestStatus.Queued:
        if (!await DependenciesReady(request, cancellationToken))
        {
          return;
        }

        try
        {
          RequestStateMachine.Move(request, RequestStatus.Applying, time.GetUtcNow());
          await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
          LogChanged(logger, requestId);
          return;
        }

        await Guarded(request, () => ApplyAsync(request, cancellationToken), cancellationToken);
        break;

      // Destroy moved it to Destroying before queueing it, so there's no move to make here.
      case RequestStatus.Destroying:
        await Guarded(request, () => DestroyAsync(request, cancellationToken), cancellationToken);
        break;

      default:
        LogNothingToDo(logger, requestId, request?.Status);
        break;
    }
  }

  /// <summary>
  /// Whether a queued request can be applied: everything it depends on has succeeded. If one never will (it failed,
  /// was cancelled, or is going or gone), the request moves to Failed without running. Otherwise it waits, and the
  /// runner queues it again when the next of its dependencies finishes.
  /// </summary>
  private async Task<bool> DependenciesReady(ProvisioningRequest request, CancellationToken cancellationToken)
  {
    var dependencyIds = await db.RequestDependencies.AsNoTracking()
      .Where(d => d.RequestId == request.Id)
      .Select(d => d.DependsOnId)
      .ToListAsync(cancellationToken);
    var dependencies = await db.ProvisioningRequests.AsNoTracking()
      .Where(x => dependencyIds.Contains(x.Id))
      .ToListAsync(cancellationToken);

    var broken = dependencies.FirstOrDefault(d =>
      !ProvisioningRequestService.ReferenceableStatuses.Contains(d.Status));
    if (broken is not null)
    {
      var link = RequestLinks.For(broken, catalog);
      await AppendLog(request.Id, LogSource.System,
        $"Failed: didn't run, because {RequestLinks.Describe(link)}, which it depends on, is {broken.Status}.",
        CancellationToken.None);
      RequestStateMachine.Move(request, RequestStatus.Failed, time.GetUtcNow());
      await db.SaveChangesAsync(CancellationToken.None);
      return false;
    }

    if (dependencies.Any(d => d.Status != RequestStatus.Succeeded))
    {
      LogWaiting(logger, request.Id);
      return false;
    }

    return true;
  }

  /// <summary>Runs a job's work, moving the request to <c>Failed</c> with a log line if it throws.</summary>
  private async Task Guarded(ProvisioningRequest request, Func<Task> work, CancellationToken cancellationToken)
  {
    try
    {
      await work();
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // Shutting down. The request stays Applying or Destroying, and the runner deals with it when it next starts.
      throw;
    }
    catch (JobFailedException ex)
    {
      await Fail(request, ex.Message);
    }
    catch (Exception ex)
    {
      LogUnexpected(logger, ex, request.Id);
      await Fail(request, "The runner hit an unexpected error; an admin can find the details in the API's logs.");
    }
  }

  private async Task ApplyAsync(ProvisioningRequest request, CancellationToken cancellationToken)
  {
    var manifest = catalog.Find(request.Type) ??
                   throw new JobFailedException($"Resource type '{request.Type}' is no longer in the catalog.");
    var environment = Environment();
    var workspace = TerraformWorkspace.Prepare(
      workspaces.Value.Root,
      catalog.GetModuleDirectory(request.Type),
      request);

    await Step(request.Id, workspace, environment, ["init", "-input=false", "-no-color"], cancellationToken);
    await Step(request.Id, workspace, environment,
      ["plan", "-input=false", "-no-color", $"-out={TerraformWorkspace.PlanFile}"], cancellationToken);
    await Step(request.Id, workspace, environment,
      ["apply", "-input=false", "-no-color", TerraformWorkspace.PlanFile], cancellationToken);

    var (plain, sensitive) = TerraformOutputs.Split(
      await ReadOutputs(request.Id, workspace, environment, cancellationToken),
      manifest);

    request.Outputs = plain.ToJsonString();
    if (sensitive.Count > 0)
    {
      db.RequestSensitiveOutputs.Add(
        new RequestSensitiveOutputs { RequestId = request.Id, Values = sensitive.ToJsonString() });
    }

    await AppendLog(request.Id, LogSource.System, "Provisioned.", CancellationToken.None);
    RequestStateMachine.Move(request, RequestStatus.Succeeded, time.GetUtcNow());
    await db.SaveChangesAsync(CancellationToken.None);
  }

  /// <summary>
  /// Runs <c>terraform destroy</c> in the request's existing workspace. It needs neither the catalog (the workspace
  /// has its own copy of the module and its tfvars) nor a fresh plan. With no state file, Terraform never created
  /// anything, so there's nothing to run.
  /// </summary>
  private async Task DestroyAsync(ProvisioningRequest request, CancellationToken cancellationToken)
  {
    var workspace = TerraformWorkspace.PathFor(workspaces.Value.Root, request.Id);
    if (File.Exists(Path.Combine(workspace, TerraformWorkspace.StateFile)))
    {
      var environment = Environment();
      await Step(request.Id, workspace, environment, ["init", "-input=false", "-no-color"], cancellationToken);
      await Step(request.Id, workspace, environment,
        ["destroy", "-auto-approve", "-input=false", "-no-color"], cancellationToken);
      await AppendLog(request.Id, LogSource.System, "Destroyed.", CancellationToken.None);
    }
    else
    {
      await AppendLog(request.Id, LogSource.System,
        "Nothing to destroy: Terraform has no state for this request, so it never created anything.",
        CancellationToken.None);
    }

    // The resource is gone, and so are the credentials its outputs held.
    request.Outputs = null;
    await db.RequestSensitiveOutputs.Where(x => x.RequestId == request.Id).ExecuteDeleteAsync(CancellationToken.None);
    RequestStateMachine.Move(request, RequestStatus.Destroyed, time.GetUtcNow());
    await db.SaveChangesAsync(CancellationToken.None);
  }

  /// <summary>
  /// The provider credentials and Terraform's automation settings. Credentials go only here, never in tfvars or the
  /// log.
  /// </summary>
  private Dictionary<string, string> Environment()
  {
    var token = digitalOcean.Value.Token;
    if (string.IsNullOrWhiteSpace(token))
    {
      throw new JobFailedException("The API has no DigitalOcean token (DigitalOcean:Token), so Terraform can't run.");
    }

    var cache = Path.Combine(Path.GetFullPath(workspaces.Value.Root), ".plugin-cache");
    Directory.CreateDirectory(cache);

    var environment = new Dictionary<string, string>
    {
      ["DIGITALOCEAN_TOKEN"] = token,
      ["TF_IN_AUTOMATION"] = "1",
      ["TF_INPUT"] = "0",
      ["CHECKPOINT_DISABLE"] = "1",

      // One shared copy of each provider instead of a download per workspace.
      ["TF_PLUGIN_CACHE_DIR"] = cache,
    };
    if (!string.IsNullOrEmpty(spaces.Value.AccessId) && !string.IsNullOrEmpty(spaces.Value.SecretKey))
    {
      environment["SPACES_ACCESS_KEY_ID"] = spaces.Value.AccessId;
      environment["SPACES_SECRET_ACCESS_KEY"] = spaces.Value.SecretKey;
    }

    return environment;
  }

  private async Task Step(
    Guid requestId,
    string workspace,
    IReadOnlyDictionary<string, string> environment,
    IReadOnlyList<string> arguments,
    CancellationToken cancellationToken)
  {
    await AppendLog(requestId, LogSource.System, $"$ terraform {string.Join(' ', arguments)}", cancellationToken);
    var exitCode = await terraform.RunAsync(
      workspace,
      arguments,
      environment,
      (stream, text) => AppendLog(requestId, stream, text, cancellationToken),
      cancellationToken);

    if (exitCode != 0)
    {
      throw new JobFailedException($"terraform {arguments[0]} exited with code {exitCode}.");
    }
  }

  /// <summary>Runs <c>terraform output -json</c>, keeping its stdout out of the log: it holds the secrets.</summary>
  private async Task<string> ReadOutputs(
    Guid requestId,
    string workspace,
    IReadOnlyDictionary<string, string> environment,
    CancellationToken cancellationToken)
  {
    await AppendLog(requestId, LogSource.System, "$ terraform output -json (values not logged)", cancellationToken);

    var json = new StringBuilder();
    var exitCode = await terraform.RunAsync(
      workspace,
      ["output", "-json", "-no-color"],
      environment,
      (stream, text) =>
      {
        if (stream == LogSource.Stdout)
        {
          json.AppendLine(text);
          return Task.CompletedTask;
        }

        return AppendLog(requestId, stream, text, cancellationToken);
      },
      cancellationToken);

    return exitCode == 0
      ? json.ToString()
      : throw new JobFailedException($"terraform output exited with code {exitCode}.");
  }

  private async Task Fail(ProvisioningRequest request, string reason)
  {
    await AppendLog(request.Id, LogSource.System, $"Failed: {reason}", CancellationToken.None);
    RequestStateMachine.Move(request, RequestStatus.Failed, time.GetUtcNow());
    await db.SaveChangesAsync(CancellationToken.None);
  }

  private async Task AppendLog(Guid requestId, LogSource stream, string text, CancellationToken cancellationToken)
  {
    var line = new JobLogLine { RequestId = requestId, At = time.GetUtcNow(), Stream = stream, Text = text };
    db.JobLogLines.Add(line);
    await db.SaveChangesAsync(cancellationToken);

    // A long apply writes thousands of lines; don't keep them all tracked.
    db.Entry(line).State = EntityState.Detached;
  }

  [LoggerMessage(Level = LogLevel.Information, Message = "Skipping the job for request {RequestId}: it's {Status}")]
  private static partial void LogNothingToDo(ILogger logger, Guid requestId, RequestStatus? status);

  [LoggerMessage(Level = LogLevel.Information,
    Message = "Request {RequestId} waits for the requests it depends on")]
  private static partial void LogWaiting(ILogger logger, Guid requestId);

  [LoggerMessage(Level = LogLevel.Information,
    Message = "Skipping the job for request {RequestId}: it changed before the runner got to it")]
  private static partial void LogChanged(ILogger logger, Guid requestId);

  [LoggerMessage(Level = LogLevel.Error, Message = "The job for request {RequestId} failed unexpectedly")]
  private static partial void LogUnexpected(ILogger logger, Exception exception, Guid requestId);
}
