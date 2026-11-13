namespace ResourceProvisioner.Api.Services.Requests;

using System.Text.Json.Nodes;
using Config;
using Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Models;
using Provisioning;
using ResourceCatalog;

/// <summary>
/// <see cref="IProvisioningRequestService"/> on the app database. Validation comes from the catalog's manifests plus
/// the checks a manifest can't express: that references name the owner's own requests of the right type, which are
/// recorded as dependencies so they're provisioned first and destroyed last.
/// </summary>
/// <param name="db">The app database.</param>
/// <param name="catalog">The resource catalog.</param>
/// <param name="limits">The active-request limits.</param>
/// <param name="jobs">The runner's queue, which a confirmed request joins.</param>
/// <param name="deleter">Deletes a cancelled request.</param>
/// <param name="time">The clock.</param>
public class ProvisioningRequestService(
  AppDbContext db,
  ICatalogService catalog,
  IOptions<LimitsOptions> limits,
  IJobDispatcher jobs,
  IRequestDeleter deleter,
  TimeProvider time) : IProvisioningRequestService
{
  /// <summary>The most requests one confirm-all can take.</summary>
  public const int MaxConfirmAtOnce = 20;

  /// <summary>The field path a problem with the request's project is reported on.</summary>
  public const string ProjectErrorKey = "project_id";

  /// <summary>
  /// Statuses a referenced request may be in: pending (it will be provisioned first) or provisioned. Failed requests
  /// and ones being destroyed can't be built on, and nor can a missing one (cancelled or destroyed, so deleted).
  /// </summary>
  public static readonly IReadOnlyList<RequestStatus> ReferenceableStatuses =
  [
    RequestStatus.AwaitingConfirmation,
    RequestStatus.Queued,
    RequestStatus.Applying,
    RequestStatus.Succeeded,
  ];

  // Counting active requests and queueing must not interleave, or two confirms could both pass the limit. The POC
  // runs one API instance, so a process-wide lock is enough.
  private static readonly SemaphoreSlim ConfirmLock = new(1, 1);

  private readonly LimitsOptions _limits = limits.Value;

  /// <inheritdoc />
  public async Task<ProposeResult> ProposeAsync(
    RequestUser owner,
    string type,
    JsonObject? variables,
    Guid? projectId,
    Guid? conversationId,
    CancellationToken cancellationToken = default)
  {
    var (values, errors, dependsOn) =
      await ValidateAsync(owner.UserId, type, variables, projectId, confirming: false, cancellationToken);
    if (errors.Count > 0)
    {
      return new ProposeResult(null, errors);
    }

    var now = time.GetUtcNow();
    var request = new ProvisioningRequest
    {
      Id = Guid.NewGuid(),
      OwnerId = owner.UserId,
      OwnerEmail = owner.Email,
      Type = type,
      Variables = values.ToJsonString(),
      Status = RequestStatus.AwaitingConfirmation,
      ProjectId = type == Project.RequestType ? null : projectId,
      ConversationId = conversationId,
      CreatedAt = now,
      UpdatedAt = now,
    };

    db.ProvisioningRequests.Add(request);
    db.RequestDependencies.AddRange(dependsOn.Select(id => new RequestDependency
    {
      RequestId = request.Id, DependsOnId = id,
    }));

    // A project request brings its project into being; the project's status is the request's from now on.
    if (type == Project.RequestType)
    {
      db.Projects.Add(new Project
      {
        Id = Guid.NewGuid(),
        OwnerId = owner.UserId,
        OwnerEmail = owner.Email,
        Name = StringValue(values, "name") ?? "",
        Description = StringValue(values, "description"),
        Purpose = StringValue(values, "purpose"),
        Environment = StringValue(values, "environment"),
        CreatedAt = now,
        ProjectRequestId = request.Id,
      });
    }

    await db.SaveChangesAsync(cancellationToken);

    return new ProposeResult((await ToResponses([request], cancellationToken))[0], errors);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<ProvisioningRequestResponse>> ListAsync(
    RequestUser user,
    Guid? projectId = null,
    CancellationToken cancellationToken = default)
  {
    var query = db.ProvisioningRequests.AsNoTracking();
    if (!user.IsAdmin)
    {
      query = query.Where(x => x.OwnerId == user.UserId);
    }

    // The requests in the project, and the one that backs it: those whose response's project is this one.
    if (projectId is { } id)
    {
      query = query.Where(x =>
        x.ProjectId == id || db.Projects.Any(p => p.Id == id && p.ProjectRequestId == x.Id));
    }

    var requests = await query.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);
    return await ToResponses(requests, cancellationToken);
  }

  /// <inheritdoc />
  public async Task<ProvisioningRequestResponse> GetAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default) =>
    (await ToResponses([await FindVisible(id, user, cancellationToken)], cancellationToken))[0];

  /// <inheritdoc />
  public async Task<ProvisioningRequestResponse> ConfirmAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default) =>
    (await ConfirmManyAsync([id], user, cancellationToken))[0];

  /// <inheritdoc />
  public async Task<IReadOnlyList<ProvisioningRequestResponse>> ConfirmManyAsync(
    IReadOnlyList<Guid> ids,
    RequestUser user,
    CancellationToken cancellationToken = default)
  {
    var distinct = ids.Distinct().ToList();
    if (distinct.Count is 0 or > MaxConfirmAtOnce)
    {
      throw new RequestConflictException($"Confirm between 1 and {MaxConfirmAtOnce} requests at once.");
    }

    var requests = new List<ProvisioningRequest>();
    foreach (var id in distinct)
    {
      var request = await FindOwn(id, user, "confirm", cancellationToken);
      EnsureCanMove(request, RequestStatus.Queued, "confirmed");
      requests.Add(request);
    }

    // The catalog may have changed since they were proposed, and a referenced request or the project may have been
    // cancelled or destroyed, and so deleted. With several requests, each error's path starts with its request ID.
    foreach (var request in requests)
    {
      var (_, errors, _) = await ValidateAsync(
        request.OwnerId,
        request.Type,
        JsonNode.Parse(request.Variables)?.AsObject(),
        request.ProjectId,
        confirming: true,
        cancellationToken);
      if (errors.Count > 0)
      {
        throw new RequestValidationException(requests.Count == 1
          ? errors
          : errors.ToDictionary(e => $"{request.Id}.{e.Key}", e => e.Value));
      }
    }

    var ordered = await InDependencyOrder(requests, cancellationToken);

    await ConfirmLock.WaitAsync(cancellationToken);
    try
    {
      await EnforceLimits(user.UserId, ordered.Count(r => catalog.Find(r.Type)?.Free != true), cancellationToken);
      var now = time.GetUtcNow();
      foreach (var request in ordered)
      {
        RequestStateMachine.Move(request, RequestStatus.Queued, now);
      }

      await Save(cancellationToken);
    }
    finally
    {
      ConfirmLock.Release();
    }

    // Saved first, so if the API stops before the runner gets to them, the runner re-queues them at startup.
    foreach (var request in ordered)
    {
      await jobs.EnqueueAsync(request.Id, cancellationToken);
    }

    var responses = await ToResponses(ordered, cancellationToken);
    return [.. distinct.Select(id => responses.Single(r => r.Id == id))];
  }

  /// <inheritdoc />
  public async Task CancelAsync(Guid id, RequestUser user, CancellationToken cancellationToken = default)
  {
    var request = await FindOwn(id, user, "cancel", cancellationToken);
    if (request.Status != RequestStatus.AwaitingConfirmation)
    {
      throw new RequestConflictException($"This request is {request.Status}, so it can't be cancelled.");
    }

    // Nothing was created, so nothing is kept. The deleter checks the status again as it deletes, so a confirm that
    // got there first wins.
    await deleter.DeleteCancelledAsync(request.Id, cancellationToken);
  }

  /// <inheritdoc />
  public async Task<JsonObject> GetSensitiveOutputsAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default)
  {
    var request = await FindVisible(id, user, cancellationToken);
    if (request.OwnerId != user.UserId)
    {
      throw new RequestForbiddenException("Only the request's owner can see its secret outputs.");
    }

    var values = await db.RequestSensitiveOutputs.AsNoTracking()
      .Where(x => x.RequestId == request.Id)
      .Select(x => x.Values)
      .SingleOrDefaultAsync(cancellationToken);
    return values is null ? [] : JsonNode.Parse(values)?.AsObject() ?? [];
  }

  /// <inheritdoc />
  public async Task<DestroyPlanResponse> GetDestroyPlanAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default)
  {
    var request = await FindVisible(id, user, cancellationToken);
    var plan = await PlanDestroy(request, cancellationToken);
    return new DestroyPlanResponse(
      plan.Reason is null,
      plan.Reason,
      [.. plan.Destroy.Select(Link)],
      [.. plan.Cancel.Select(Link)],
      [.. plan.WaitFor.Select(Link)]);
  }

  /// <inheritdoc />
  public async Task<ProvisioningRequestResponse> DestroyAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default)
  {
    var request = await FindVisible(id, user, cancellationToken);
    var plan = await PlanDestroy(request, cancellationToken);
    if (plan.Reason is not null)
    {
      throw new RequestConflictException(plan.Reason);
    }

    // All in one transaction: the proposals go, and the request and everything that may exist above it move to
    // Destroying, or nothing changes. Every move and delete checks the status it read, so a confirm or a runner job
    // that got in between makes the whole destroy fail with 409.
    await using (var transaction = await db.Database.BeginTransactionAsync(cancellationToken))
    {
      foreach (var proposal in plan.Cancel)
      {
        await deleter.DeleteCancelledAsync(proposal.Id, cancellationToken);
        db.Entry(proposal).State = EntityState.Detached;
      }

      var now = time.GetUtcNow();
      foreach (var target in plan.Destroy)
      {
        RequestStateMachine.Move(target, RequestStatus.Destroying, now);
      }

      await Save(cancellationToken);
      await transaction.CommitAsync(cancellationToken);
    }

    // Saved first: if the API stops before the runner gets to them, the runner re-queues them at startup. Each waits
    // for what depends on it to go, so the order is only a head start.
    foreach (var target in plan.Destroy)
    {
      await jobs.EnqueueAsync(target.Id, cancellationToken);
    }

    return (await ToResponses([request], cancellationToken))[0];
  }

  /// <summary>
  /// Works out a cascading destroy of <paramref name="request"/>: everything that depends on it, directly or not, in
  /// teardown order, split into what's destroyed (it and anything that may exist: Succeeded, Failed, DestroyFailed),
  /// what's cancelled (proposals) and what's still in the runner's hands (Queued, Applying, Destroying), which refuses
  /// the destroy until it's finished. The requests are tracked, so <see cref="DestroyAsync"/> can move them.
  /// </summary>
  private async Task<DestroyPlan> PlanDestroy(ProvisioningRequest request, CancellationToken cancellationToken)
  {
    var dependents = await RequestGraph.DependentsInTeardownOrderAsync(db, request, cancellationToken);
    var cancel = dependents.Where(d => d.Status == RequestStatus.AwaitingConfirmation).ToList();
    var waitFor = dependents
      .Where(d => d.Status is RequestStatus.Queued or RequestStatus.Applying or RequestStatus.Destroying)
      .ToList();
    List<ProvisioningRequest> destroy =
    [
      .. dependents.Where(d => RequestStateMachine.DestroyableStatuses.Contains(d.Status)),
      request,
    ];

    var reason = !RequestStateMachine.CanMove(request.Status, RequestStatus.Destroying)
      ? $"This request is {request.Status}, so it can't be destroyed."
      : waitFor.Count > 0
        ? $"Wait for {string.Join(", ", waitFor.Select(w => RequestLinks.Describe(Link(w))))} to finish, then try " +
          "again: it depends on this request."
        : null;
    return new DestroyPlan(destroy, cancel, waitFor, reason);
  }

  private RequestLink Link(ProvisioningRequest request) => RequestLinks.For(request, catalog);

  /// <summary>
  /// The manifest's checks, the project, then the references in the values that passed them. Returns the values with
  /// defaults applied, every error by field path, and the requests it depends on: its project's request first, then
  /// those it references. A project or reference that's missing (cancelled or destroyed, so deleted) is an error, as
  /// is one that's failed or being destroyed.
  /// </summary>
  private async Task<(JsonObject Values, Dictionary<string, string[]> Errors, List<Guid> DependsOn)> ValidateAsync(
    string ownerId,
    string type,
    JsonObject? variables,
    Guid? projectId,
    bool confirming,
    CancellationToken cancellationToken)
  {
    var result = catalog.ValidateVariables(type, variables);
    var errors = result.Errors.ToDictionary(e => e.Key, e => e.Value);
    var dependsOn = new List<Guid>();

    if (catalog.Find(type) is { } manifest)
    {
      var (projectError, projectRequestId) =
        await CheckProject(ownerId, type, projectId, confirming, cancellationToken);
      if (projectError is not null)
      {
        errors[ProjectErrorKey] = [projectError];
      }
      else if (projectRequestId is { } id)
      {
        dependsOn.Add(id);
      }

      var (problems, referenced) = await CheckReferences(manifest, result, ownerId, cancellationToken);
      foreach (var (path, message) in problems)
      {
        errors[path] = [.. errors.GetValueOrDefault(path, []), message];
      }

      dependsOn.AddRange(referenced.Except(dependsOn).ToList());
    }

    return (result.Values, errors, dependsOn);
  }

  /// <summary>
  /// A <c>project</c> request belongs to no project; any other must belong to one of the owner's own projects, admins
  /// included, whose request is pending or provisioned (<see cref="ReferenceableStatuses"/>), as references are
  /// checked. Someone else's project and a missing one get the same message. Returns the error, or the project's
  /// request, which the new request depends on.
  /// </summary>
  private async Task<(string? Error, Guid? ProjectRequestId)> CheckProject(
    string ownerId,
    string type,
    Guid? projectId,
    bool confirming,
    CancellationToken cancellationToken)
  {
    if (type == Project.RequestType)
    {
      return projectId is null ? (null, null) : ("A project can't belong to another project; leave it out.", null);
    }

    if (projectId is not { } id)
    {
      // At confirm, a request with no project is from before projects, or its project was cancelled or destroyed
      // (and so deleted) while it waited.
      return (confirming
        ? "It isn't in a project any more (its project may have been cancelled or destroyed); ask for it again in " +
          "one of your projects."
        : "Required: the ID of one of your projects to put this in.", null);
    }

    var project = await db.Projects.AsNoTracking()
      .Where(p => p.Id == id && p.OwnerId == ownerId)
      .Join(db.ProvisioningRequests, p => p.ProjectRequestId, r => r.Id, (p, r) => new { p.Name, r.Id, r.Status })
      .SingleOrDefaultAsync(cancellationToken);
    if (project is null)
    {
      return ($"'{id}' isn't the ID of one of your projects.", null);
    }

    return ReferenceableStatuses.Contains(project.Status)
      ? (null, project.Id)
      : ($"Project \"{project.Name}\" is {project.Status}; only pending or provisioned projects can be used.", null);
  }

  private static string? StringValue(JsonObject values, string name) =>
    values[name] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;

  /// <summary>
  /// Every request ID in a variable with <c>references</c> must be one of the owner's own requests of that type,
  /// pending or provisioned (<see cref="ReferenceableStatuses"/>), admins included. Someone else's request and a
  /// missing one get the same message, so the check doesn't reveal other users' request IDs.
  /// </summary>
  private async Task<(List<(string Path, string Message)> Problems, List<Guid> Referenced)> CheckReferences(
    ResourceManifest manifest,
    VariableValidationResult result,
    string ownerId,
    CancellationToken cancellationToken)
  {
    var references = new List<(string Path, string Value, string Type)>();
    foreach (var variable in manifest.Variables.Where(v => v.References is not null))
    {
      // A variable with its own errors (wrong shape, bad pattern) is already reported.
      if (result.Errors.Keys.Any(k =>
            k == variable.Name || k.StartsWith($"{variable.Name}[", StringComparison.Ordinal)))
      {
        continue;
      }

      switch (result.Values[variable.Name])
      {
        case JsonArray items:
          for (var i = 0; i < items.Count; i++)
          {
            references.Add(($"{variable.Name}[{i}]", items[i]?.GetValue<string>() ?? "", variable.References!));
          }

          break;
        case JsonValue value when value.TryGetValue<string>(out var single) && single.Length > 0:
          references.Add((variable.Name, single, variable.References!));
          break;
      }
    }

    if (references.Count == 0)
    {
      return ([], []);
    }

    var ids = references.Select(r => Guid.TryParse(r.Value, out var id) ? id : Guid.Empty).Distinct().ToList();
    var found = await db.ProvisioningRequests.AsNoTracking()
      .Where(x => ids.Contains(x.Id) && x.OwnerId == ownerId)
      .ToDictionaryAsync(x => x.Id, x => new { x.Type, x.Status }, cancellationToken);

    var problems = new List<(string Path, string Message)>();
    var referenced = new List<Guid>();
    foreach (var (path, value, type) in references)
    {
      var displayName = catalog.Find(type)?.DisplayName ?? type;
      if (!Guid.TryParse(value, out var id) || !found.TryGetValue(id, out var target) || target.Type != type)
      {
        problems.Add((path, $"'{value}' isn't the request ID of one of your {displayName} requests."));
      }
      else if (!ReferenceableStatuses.Contains(target.Status))
      {
        problems.Add((path, $"{displayName} request {value} is {target.Status}; " +
                            "only pending or provisioned requests can be referenced."));
      }
      else if (!referenced.Contains(id))
      {
        referenced.Add(id);
      }
    }

    return (problems, referenced);
  }

  /// <summary>
  /// Orders requests so each comes after the ones in the set it depends on (oldest first among equals), and refuses
  /// if one depends on a request still awaiting confirmation outside the set.
  /// </summary>
  private async Task<List<ProvisioningRequest>> InDependencyOrder(
    List<ProvisioningRequest> requests,
    CancellationToken cancellationToken)
  {
    var inSet = requests.Select(r => r.Id).ToHashSet();
    var dependencies = await db.RequestDependencies.AsNoTracking()
      .Where(d => inSet.Contains(d.RequestId))
      .ToListAsync(cancellationToken);

    var outsideIds = dependencies.Select(d => d.DependsOnId).Where(id => !inSet.Contains(id)).Distinct().ToList();
    var unconfirmed = await db.ProvisioningRequests.AsNoTracking()
      .Where(x => outsideIds.Contains(x.Id) && x.Status == RequestStatus.AwaitingConfirmation)
      .ToListAsync(cancellationToken);
    if (unconfirmed.Count > 0)
    {
      throw new RequestConflictException(
        $"Confirm {string.Join(", ", unconfirmed.Select(u => RequestLinks.Describe(RequestLinks.For(u, catalog))))} " +
        "first, or together with this: it's provisioned before it.");
    }

    var ordered = new List<ProvisioningRequest>();
    var remaining = requests.OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).ToList();
    while (remaining.Count > 0)
    {
      // References only point at earlier requests, so there's always one with nothing left to wait for.
      var next = remaining.First(r => dependencies
        .Where(d => d.RequestId == r.Id && inSet.Contains(d.DependsOnId))
        .All(d => ordered.Any(o => o.Id == d.DependsOnId)));
      ordered.Add(next);
      remaining.Remove(next);
    }

    return ordered;
  }

  /// <summary>
  /// Checks the active-request limits for confirming <paramref name="adding"/> more requests. Free types (projects,
  /// firewalls) count neither among the active requests nor among those being confirmed.
  /// </summary>
  private async Task EnforceLimits(string ownerId, int adding, CancellationToken cancellationToken)
  {
    if (adding == 0)
    {
      return;
    }

    var free = catalog.Types.Where(t => t.Free).Select(t => t.Id).ToList();
    var active = db.ProvisioningRequests.Where(x =>
      RequestStateMachine.ActiveStatuses.Contains(x.Status) && !free.Contains(x.Type));

    var mine = await active.CountAsync(x => x.OwnerId == ownerId, cancellationToken);
    if (mine + adding > _limits.MaxActiveRequestsPerUser)
    {
      throw new RequestLimitException(adding == 1
        ? $"You already have {mine} active requests, and each user can have at most " +
          $"{_limits.MaxActiveRequestsPerUser}. Destroy one before confirming another."
        : $"You have {mine} active requests; confirming these {adding} would make {mine + adding}, over the " +
          $"limit of {_limits.MaxActiveRequestsPerUser} per user. Destroy something first, or confirm fewer.");
    }

    var total = await active.CountAsync(cancellationToken);
    if (total + adding > _limits.MaxActiveRequestsTotal)
    {
      throw new RequestLimitException(adding == 1
        ? $"The app already has {total} active requests across all users, its limit of " +
          $"{_limits.MaxActiveRequestsTotal}. Try again once some have been destroyed."
        : $"The app has {total} active requests across all users; confirming these {adding} would go over its " +
          $"limit of {_limits.MaxActiveRequestsTotal}. Try again once some have been destroyed, or confirm fewer.");
    }
  }

  private static void EnsureCanMove(ProvisioningRequest request, RequestStatus to, string verb)
  {
    if (!RequestStateMachine.CanMove(request.Status, to))
    {
      throw new RequestConflictException($"This request is {request.Status}, so it can't be {verb}.");
    }
  }

  private async Task Save(CancellationToken cancellationToken)
  {
    try
    {
      await db.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
      throw new RequestConflictException("A request changed while this was being done. Reload it and try again.");
    }
  }

  /// <summary>A request the user may see: their own, or any for an admin. Otherwise it doesn't exist for them.</summary>
  private async Task<ProvisioningRequest> FindVisible(Guid id, RequestUser user, CancellationToken cancellationToken)
  {
    var request = await db.ProvisioningRequests.SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
    return request is not null && (request.OwnerId == user.UserId || user.IsAdmin)
      ? request
      : throw new KeyNotFoundException($"Request {id} doesn't exist.");
  }

  /// <summary>A request the user may act on: only their own, admins included.</summary>
  private async Task<ProvisioningRequest> FindOwn(
    Guid id,
    RequestUser user,
    string action,
    CancellationToken cancellationToken)
  {
    var request = await FindVisible(id, user, cancellationToken);
    return request.OwnerId == user.UserId
      ? request
      : throw new RequestForbiddenException($"Only the request's owner can {action} it.");
  }

  /// <summary>
  /// Responses for requests, with what each depends on and what depends on it, and its project (the one it belongs
  /// to, or backs).
  /// </summary>
  private async Task<List<ProvisioningRequestResponse>> ToResponses(
    IReadOnlyList<ProvisioningRequest> requests,
    CancellationToken cancellationToken)
  {
    var ids = requests.Select(r => r.Id).ToList();
    var projectIds = requests.Where(r => r.ProjectId is not null).Select(r => r.ProjectId!.Value).Distinct().ToList();
    var projects = await db.Projects.AsNoTracking()
      .Where(p => projectIds.Contains(p.Id) || ids.Contains(p.ProjectRequestId))
      .Select(p => new { p.Id, p.Name, p.ProjectRequestId })
      .ToListAsync(cancellationToken);
    ProjectLink? ProjectOf(ProvisioningRequest r) =>
      projects.Where(p => r.ProjectId is { } id ? p.Id == id : p.ProjectRequestId == r.Id)
        .Select(p => new ProjectLink(p.Id, p.Name))
        .FirstOrDefault();

    var dependencies = await db.RequestDependencies.AsNoTracking()
      .Where(d => ids.Contains(d.RequestId) || ids.Contains(d.DependsOnId))
      .ToListAsync(cancellationToken);

    // Prefer the given requests, whose status may have just changed, over a fresh read.
    var linkedIds = dependencies.SelectMany(d => new[] { d.RequestId, d.DependsOnId })
      .Concat(requests.Where(r => r.DestroyStoppedById is not null).Select(r => r.DestroyStoppedById!.Value))
      .Except(ids)
      .Distinct()
      .ToList();
    var linked = (await db.ProvisioningRequests.AsNoTracking()
        .Where(x => linkedIds.Contains(x.Id))
        .ToListAsync(cancellationToken))
      .Concat(requests)
      .ToDictionary(x => x.Id, x => RequestLinks.For(x, catalog));

    return [.. requests.Select(r => ToResponse(
      r,
      ProjectOf(r),
      [.. dependencies.Where(d => d.RequestId == r.Id).Select(d => linked[d.DependsOnId])],
      [.. dependencies.Where(d => d.DependsOnId == r.Id).Select(d => linked[d.RequestId])],
      r.DestroyStoppedById is { } stoppedBy ? linked.GetValueOrDefault(stoppedBy) : null))];
  }

  private ProvisioningRequestResponse ToResponse(
    ProvisioningRequest x,
    ProjectLink? project,
    IReadOnlyList<RequestLink> dependsOn,
    IReadOnlyList<RequestLink> dependents,
    RequestLink? destroyStoppedBy)
  {
    var manifest = catalog.Find(x.Type);

    // Sensitive variables are supplied by the server and never shown.
    var variables = JsonNode.Parse(x.Variables)?.AsObject() ?? [];
    foreach (var sensitive in manifest?.Variables.Where(v => v.Sensitive) ?? [])
    {
      variables.Remove(sensitive.Name);
    }

    return new ProvisioningRequestResponse(
      x.Id,
      x.OwnerId,
      x.OwnerEmail,
      x.Type,
      manifest?.DisplayName ?? x.Type,
      x.Status,
      variables,
      x.Outputs is null ? null : JsonNode.Parse(x.Outputs)?.AsObject(),
      project,
      x.ConversationId,
      x.CreatedAt,
      x.UpdatedAt,
      x.ConfirmedAt,
      dependsOn,
      dependents,
      destroyStoppedBy);
  }

  /// <summary>A cascading destroy worked out by <see cref="PlanDestroy"/>.</summary>
  /// <param name="Destroy">What moves to Destroying, in teardown order, the request itself last.</param>
  /// <param name="Cancel">The proposals that depend on it, which are deleted.</param>
  /// <param name="WaitFor">What depends on it and is in the runner's hands.</param>
  /// <param name="Reason">Why the destroy is refused, or null.</param>
  private sealed record DestroyPlan(
    List<ProvisioningRequest> Destroy,
    List<ProvisioningRequest> Cancel,
    List<ProvisioningRequest> WaitFor,
    string? Reason);
}
