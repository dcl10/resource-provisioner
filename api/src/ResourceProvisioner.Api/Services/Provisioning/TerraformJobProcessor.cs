namespace ResourceProvisioner.Api.Services.Provisioning;

using System.Text;
using System.Text.Json.Nodes;
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
/// <c>workspace select -or-create=true &lt;requestId&gt;</c>, <c>plan -out=plan.tfplan</c>, <c>apply plan.tfplan</c>
/// and <c>output -json</c>; a destroy prepares the workspace again from the catalog and the tfvars the apply saved,
/// then runs <c>init</c>, <c>workspace select &lt;requestId&gt;</c> and <c>destroy -auto-approve</c>. State is in the
/// app database (<see cref="ITerraformStateStore"/>), in a Terraform workspace named by the request ID, so nothing on
/// disk outlives a job. Every line of output is saved as a <see cref="JobLogLine"/>
/// except <c>output</c>'s, which holds secrets. Both wait for other requests: an apply until everything it depends on
/// has succeeded, a destroy until everything that depends on it has gone.
/// </summary>
/// <param name="db">The app database.</param>
/// <param name="catalog">The resource catalog.</param>
/// <param name="terraform">Runs the CLI.</param>
/// <param name="stateStore">Where Terraform keeps state.</param>
/// <param name="deleter">Deletes a request once it's destroyed.</param>
/// <param name="workspaces">Where workspaces go.</param>
/// <param name="digitalOcean">The DigitalOcean token, passed to Terraform in its environment.</param>
/// <param name="spaces">The Spaces keys, passed to Terraform in its environment.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">Logs what the user doesn't see.</param>
public partial class TerraformJobProcessor(
  AppDbContext db,
  ICatalogService catalog,
  ITerraformProcessRunner terraform,
  ITerraformStateStore stateStore,
  IRequestDeleter deleter,
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

        await Guarded(
          request,
          () => ApplyAsync(request, cancellationToken),
          reason => Fail(request, reason),
          cancellationToken);
        break;

      // Destroy moved it to Destroying before queueing it, so there's no move to make here.
      case RequestStatus.Destroying:
        if (!await ReadyToDestroy(request, cancellationToken))
        {
          return;
        }

        await Guarded(
          request,
          () => DestroyAsync(request, cancellationToken),
          reason => FailDestroy(request, reason),
          cancellationToken);
        break;

      default:
        LogNothingToDo(logger, requestId, request?.Status);
        break;
    }
  }

  /// <summary>
  /// The runner variables a request takes from what it depends on (<see cref="RunnerVariables.ForDependencies"/>),
  /// such as <c>rp_project_id</c> from its project's request, if its module declares it. Its dependencies, its
  /// project's request included, have all succeeded by now.
  /// </summary>
  private async Task<JsonObject> DependencyVariables(
    ProvisioningRequest request,
    ResourceManifest manifest,
    CancellationToken cancellationToken)
  {
    var dependencyIds = await db.RequestDependencies.AsNoTracking()
      .Where(d => d.RequestId == request.Id)
      .Select(d => d.DependsOnId)
      .ToListAsync(cancellationToken);

    Guid? projectRequestId = null;
    if (request.ProjectId is { } projectId &&
        catalog.ModuleDeclares(request.Type, RunnerVariables.ProjectIdVariable))
    {
      projectRequestId = await db.Projects.AsNoTracking()
                           .Where(p => p.Id == projectId)
                           .Select(p => (Guid?)p.ProjectRequestId)
                           .SingleOrDefaultAsync(cancellationToken) ??
                         throw new JobFailedException($"Project {projectId}, which it belongs to, no longer exists.");
      dependencyIds.Add(projectRequestId.Value);
    }

    var outputs = await db.ProvisioningRequests.AsNoTracking()
      .Where(x => dependencyIds.Contains(x.Id))
      .ToDictionaryAsync(x => x.Id, x => x.Outputs, cancellationToken);

    try
    {
      return RunnerVariables.ForDependencies(
        manifest,
        JsonNode.Parse(request.Variables)!.AsObject(),
        outputs,
        projectRequestId);
    }
    catch (InvalidOperationException ex)
    {
      throw new JobFailedException(ex.Message);
    }
  }

  /// <summary>
  /// Whether a queued request can be applied: everything it depends on has succeeded. If one never will (it failed,
  /// is being destroyed, or no longer exists), the request moves to Failed without running. Otherwise it waits, and the
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

    // A missing one was cancelled or destroyed, and so deleted. Deleting a request deletes the dependencies on it, so
    // this only catches one deleted between the two reads, but it fails the request just as a destroyed one did.
    var missing = dependencyIds.Except(dependencies.Select(d => d.Id)).FirstOrDefault();
    var broken = dependencies.FirstOrDefault(d =>
      !ProvisioningRequestService.ReferenceableStatuses.Contains(d.Status));
    var reason = missing != Guid.Empty
      ? $"request {missing}, which it depends on, no longer exists"
      : broken is not null
        ? $"{RequestLinks.Describe(RequestLinks.For(broken, catalog))}, which it depends on, is {broken.Status}"
        : null;
    if (reason is not null)
    {
      await AppendLog(request.Id, LogSource.System, $"Failed: didn't run, because {reason}.", CancellationToken.None);
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

  /// <summary>
  /// Whether a destroying request can be destroyed now: nothing that may exist depends on it any more. Its
  /// dependents in the same cascade go first, so while one is still in the runner's hands it waits, and the runner
  /// queues it again when that one's job ends. If one is settled instead (it failed to destroy, or is otherwise still
  /// there), it never will be, so its destroy stops: it and whatever is waiting on it go back to the status they had,
  /// untouched. Proposals don't count; deleting it unlinks them.
  /// </summary>
  private async Task<bool> ReadyToDestroy(ProvisioningRequest request, CancellationToken cancellationToken)
  {
    var dependentIds = await RequestGraph.DependentIdsAsync(db, [request.Id], cancellationToken);
    var dependents = await db.ProvisioningRequests.AsNoTracking()
      .Where(x => dependentIds.Contains(x.Id) && x.Status != RequestStatus.AwaitingConfirmation)
      .OrderBy(x => x.CreatedAt)
      .ToListAsync(cancellationToken);
    if (dependents.Count == 0)
    {
      return true;
    }

    // The eager stop in FailDestroy normally gets here first; this catches what it couldn't, such as a dependent
    // that went back before this request's job ran.
    if (dependents.FirstOrDefault(d => RequestStateMachine.DestroyableStatuses.Contains(d.Status)) is { } settled)
    {
      var line = settled.Status == RequestStatus.DestroyFailed
        ? StoppedLine(settled)
        : $"Destroy stopped: {RequestLinks.Describe(RequestLinks.For(settled, catalog))}, which depends on it, is " +
          $"{settled.Status}, so it isn't being destroyed.";
      await StopDestroys([request.Id], settled.Id, line, cancellationToken);
      await db.SaveChangesAsync(CancellationToken.None);
      return false;
    }

    LogWaitingForDependents(logger, request.Id);
    return false;
  }

  /// <summary>
  /// Moves the given requests, if they're Destroying, and everything Destroying that they depend on, directly or
  /// not, back to the status each had (<see cref="RequestStateMachine.RevertDestroy"/>), with
  /// <paramref name="line"/> in each one's log. They were waiting for <paramref name="stoppedById"/> to go, and it
  /// won't, so none of them can be destroyed; nothing was done to them. Not saved: the caller saves it together with
  /// whatever stopped them.
  /// </summary>
  private async Task StopDestroys(
    List<Guid> ids,
    Guid stoppedById,
    string line,
    CancellationToken cancellationToken)
  {
    var seen = new HashSet<Guid>();
    var now = time.GetUtcNow();
    var frontier = ids;
    while (frontier.Count > 0)
    {
      var waiting = await db.ProvisioningRequests
        .Where(x => frontier.Contains(x.Id) && x.Status == RequestStatus.Destroying)
        .ToListAsync(cancellationToken);
      waiting = [.. waiting.Where(w => seen.Add(w.Id))];
      foreach (var request in waiting)
      {
        RequestStateMachine.RevertDestroy(request, stoppedById, now);
        db.JobLogLines.Add(new JobLogLine { RequestId = request.Id, At = now, Stream = LogSource.System, Text = line });
      }

      frontier = waiting.Count == 0
        ? []
        : await RequestGraph.DependencyIdsAsync(db, [.. waiting.Select(w => w.Id)], cancellationToken);
    }
  }

  private string StoppedLine(ProvisioningRequest failed) =>
    $"Destroy stopped: {RequestLinks.Describe(RequestLinks.For(failed, catalog))} failed to destroy.";

  /// <summary>Runs a job's work, calling <paramref name="fail"/> with the reason if it throws.</summary>
  private async Task Guarded(
    ProvisioningRequest request,
    Func<Task> work,
    Func<string, Task> fail,
    CancellationToken cancellationToken)
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
      await fail(ex.Message);
    }
    catch (Exception ex)
    {
      LogUnexpected(logger, ex, request.Id);
      await fail("The runner hit an unexpected error; an admin can find the details in the API's logs.");
    }
  }

  private async Task ApplyAsync(ProvisioningRequest request, CancellationToken cancellationToken)
  {
    var manifest = catalog.Find(request.Type) ??
                   throw new JobFailedException($"Resource type '{request.Type}' is no longer in the catalog.");
    if (await stateStore.HasStateAsync(request.Id, cancellationToken))
    {
      throw new JobFailedException(
        $"Terraform already has state for request {request.Id}, so it wasn't applied again.");
    }

    var environment = Environment();

    // Saved before Terraform runs, so even a half-done apply can be destroyed with the same variables.
    request.TerraformVariables = TerraformWorkspace.BuildVariables(
      request,
      await DependencyVariables(request, manifest, cancellationToken));
    await db.SaveChangesAsync(cancellationToken);

    var workspace = TerraformWorkspace.Prepare(
      workspaces.Value.Root,
      catalog.GetModuleDirectory(request.Type),
      request.Id,
      request.TerraformVariables);

    await Step(request.Id, workspace, environment, ["init", "-input=false", "-no-color"], cancellationToken);
    await Step(request.Id, workspace, environment,
      ["workspace", "select", "-or-create=true", "-no-color", request.Id.ToString()], cancellationToken);
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
  /// Runs <c>terraform destroy</c> in a workspace prepared again from the type's catalog module and the tfvars the
  /// apply saved, so it needs nothing an earlier job left on disk. With no state, Terraform never created anything, so
  /// there's nothing to run.
  /// </summary>
  private async Task DestroyAsync(ProvisioningRequest request, CancellationToken cancellationToken)
  {
    if (await stateStore.HasStateAsync(request.Id, cancellationToken))
    {
      var cleanUp = $"an admin can delete its resources in DigitalOcean by the tag rp-{request.Id}.";
      if (catalog.Find(request.Type) is null)
      {
        throw new JobFailedException(
          $"Resource type '{request.Type}' is no longer in the catalog, so Terraform can't destroy it; {cleanUp}");
      }

      if (request.TerraformVariables is null)
      {
        throw new JobFailedException(
          $"The variables its apply used weren't saved, so Terraform can't destroy it; {cleanUp}");
      }

      var environment = Environment();
      var workspace = TerraformWorkspace.Prepare(
        workspaces.Value.Root,
        catalog.GetModuleDirectory(request.Type),
        request.Id,
        request.TerraformVariables);
      await Step(request.Id, workspace, environment, ["init", "-input=false", "-no-color"], cancellationToken);
      await Step(request.Id, workspace, environment,
        ["workspace", "select", "-no-color", request.Id.ToString()], cancellationToken);
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

    // Nothing is left, so nothing is kept: the request goes, with its outputs, log, dependencies, project and state,
    // in one transaction. Not cancellable: the resources are already gone.
    await deleter.DeleteDestroyedAsync(request.Id, CancellationToken.None);
    db.Entry(request).State = EntityState.Detached;
  }

  /// <summary>
  /// The provider and state backend credentials, and Terraform's automation settings. Credentials go only here, never
  /// in tfvars or the log.
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
    foreach (var (name, value) in stateStore.BackendEnvironment())
    {
      environment[name] = value;
    }

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

  /// <summary>
  /// A destroy that failed, or couldn't run: the request moves to DestroyFailed keeping everything, its state above
  /// all, since its resources may still exist. What was waiting for it to go (what it depends on, in the same
  /// cascade) can't be destroyed now, so those go back to the status they had, all in one save.
  /// </summary>
  private async Task FailDestroy(ProvisioningRequest request, string reason)
  {
    await AppendLog(request.Id, LogSource.System, $"Failed: {reason}", CancellationToken.None);

    // A failed delete after a successful destroy may have left it detached.
    if (db.Entry(request).State == EntityState.Detached)
    {
      request = await db.ProvisioningRequests.SingleAsync(x => x.Id == request.Id, CancellationToken.None);
    }

    RequestStateMachine.Move(request, RequestStatus.DestroyFailed, time.GetUtcNow());
    await StopDestroys(
      await RequestGraph.DependencyIdsAsync(db, [request.Id], CancellationToken.None),
      request.Id,
      StoppedLine(request),
      CancellationToken.None);
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
    Message = "Request {RequestId} waits for the requests that depend on it to be destroyed")]
  private static partial void LogWaitingForDependents(ILogger logger, Guid requestId);

  [LoggerMessage(Level = LogLevel.Information,
    Message = "Skipping the job for request {RequestId}: it changed before the runner got to it")]
  private static partial void LogChanged(ILogger logger, Guid requestId);

  [LoggerMessage(Level = LogLevel.Error, Message = "The job for request {RequestId} failed unexpectedly")]
  private static partial void LogUnexpected(ILogger logger, Exception exception, Guid requestId);
}
