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
/// <param name="time">The clock.</param>
public class ProvisioningRequestService(
  AppDbContext db,
  ICatalogService catalog,
  IOptions<LimitsOptions> limits,
  IJobDispatcher jobs,
  TimeProvider time) : IProvisioningRequestService
{
  /// <summary>The most requests one confirm-all can take.</summary>
  public const int MaxConfirmAtOnce = 20;

  /// <summary>
  /// Statuses a referenced request may be in: pending (it will be provisioned first) or provisioned. Failed,
  /// cancelled and destroyed requests, and ones being destroyed, can't be built on.
  /// </summary>
  public static readonly IReadOnlyList<RequestStatus> ReferenceableStatuses =
  [
    RequestStatus.AwaitingConfirmation,
    RequestStatus.Queued,
    RequestStatus.Applying,
    RequestStatus.Succeeded,
  ];

  /// <summary>
  /// Statuses of a dependent that stop its dependency being destroyed: it exists, may exist (a failed apply), or is
  /// about to.
  /// </summary>
  public static readonly IReadOnlyList<RequestStatus> BlockingDependentStatuses =
  [
    RequestStatus.Queued,
    RequestStatus.Applying,
    RequestStatus.Succeeded,
    RequestStatus.Failed,
    RequestStatus.Destroying,
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
    Guid? conversationId,
    CancellationToken cancellationToken = default)
  {
    var (values, errors, dependsOn) = await ValidateAsync(owner.UserId, type, variables, cancellationToken);
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
      ConversationId = conversationId,
      CreatedAt = now,
      UpdatedAt = now,
    };

    db.ProvisioningRequests.Add(request);
    db.RequestDependencies.AddRange(dependsOn.Select(id => new RequestDependency
    {
      RequestId = request.Id, DependsOnId = id,
    }));
    await db.SaveChangesAsync(cancellationToken);

    return new ProposeResult((await ToResponses([request], cancellationToken))[0], errors);
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<ProvisioningRequestResponse>> ListAsync(
    RequestUser user,
    CancellationToken cancellationToken = default)
  {
    var query = db.ProvisioningRequests.AsNoTracking();
    if (!user.IsAdmin)
    {
      query = query.Where(x => x.OwnerId == user.UserId);
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

    // The catalog may have changed since they were proposed, and a referenced request may have been cancelled or
    // destroyed. With several requests, each error's path starts with its request ID.
    foreach (var request in requests)
    {
      var (_, errors, _) = await ValidateAsync(
        request.OwnerId,
        request.Type,
        JsonNode.Parse(request.Variables)?.AsObject(),
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
  public async Task<ProvisioningRequestResponse> CancelAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default)
  {
    var request = await FindOwn(id, user, "cancel", cancellationToken);
    EnsureCanMove(request, RequestStatus.Cancelled, "cancelled");

    RequestStateMachine.Move(request, RequestStatus.Cancelled, time.GetUtcNow());
    await Save(cancellationToken);

    return (await ToResponses([request], cancellationToken))[0];
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
  public async Task<ProvisioningRequestResponse> DestroyAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default)
  {
    var request = await FindVisible(id, user, cancellationToken);
    EnsureCanMove(request, RequestStatus.Destroying, "destroyed");

    var dependentIds = await db.RequestDependencies.AsNoTracking()
      .Where(d => d.DependsOnId == request.Id)
      .Select(d => d.RequestId)
      .ToListAsync(cancellationToken);
    var blocking = await db.ProvisioningRequests.AsNoTracking()
      .Where(x => dependentIds.Contains(x.Id) && BlockingDependentStatuses.Contains(x.Status))
      .ToListAsync(cancellationToken);
    if (blocking.Count > 0)
    {
      throw new RequestConflictException(
        "Destroy what depends on it first: " +
        $"{string.Join(", ", blocking.Select(b => RequestLinks.Describe(RequestLinks.For(b, catalog))))}.");
    }

    RequestStateMachine.Move(request, RequestStatus.Destroying, time.GetUtcNow());
    await Save(cancellationToken);

    // Saved first: if the API stops before the runner gets to it, the runner re-queues it at startup.
    await jobs.EnqueueAsync(request.Id, cancellationToken);
    return (await ToResponses([request], cancellationToken))[0];
  }

  /// <summary>
  /// The manifest's checks, then the references in the values that passed them. Returns the values with defaults
  /// applied, every error by field path, and the requests referenced.
  /// </summary>
  private async Task<(JsonObject Values, Dictionary<string, string[]> Errors, List<Guid> DependsOn)> ValidateAsync(
    string ownerId,
    string type,
    JsonObject? variables,
    CancellationToken cancellationToken)
  {
    var result = catalog.ValidateVariables(type, variables);
    var errors = result.Errors.ToDictionary(e => e.Key, e => e.Value);
    var dependsOn = new List<Guid>();

    if (catalog.Find(type) is { } manifest)
    {
      var (problems, referenced) = await CheckReferences(manifest, result, ownerId, cancellationToken);
      foreach (var (path, message) in problems)
      {
        errors[path] = [.. errors.GetValueOrDefault(path, []), message];
      }

      dependsOn.AddRange(referenced);
    }

    return (result.Values, errors, dependsOn);
  }

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

  /// <summary>Responses for requests, with what each depends on and what depends on it.</summary>
  private async Task<List<ProvisioningRequestResponse>> ToResponses(
    IReadOnlyList<ProvisioningRequest> requests,
    CancellationToken cancellationToken)
  {
    var ids = requests.Select(r => r.Id).ToList();
    var dependencies = await db.RequestDependencies.AsNoTracking()
      .Where(d => ids.Contains(d.RequestId) || ids.Contains(d.DependsOnId))
      .ToListAsync(cancellationToken);

    // Prefer the given requests, whose status may have just changed, over a fresh read.
    var linkedIds = dependencies.SelectMany(d => new[] { d.RequestId, d.DependsOnId }).Except(ids).Distinct().ToList();
    var linked = (await db.ProvisioningRequests.AsNoTracking()
        .Where(x => linkedIds.Contains(x.Id))
        .ToListAsync(cancellationToken))
      .Concat(requests)
      .ToDictionary(x => x.Id, x => RequestLinks.For(x, catalog));

    return [.. requests.Select(r => ToResponse(
      r,
      [.. dependencies.Where(d => d.RequestId == r.Id).Select(d => linked[d.DependsOnId])],
      [.. dependencies.Where(d => d.DependsOnId == r.Id).Select(d => linked[d.RequestId])]))];
  }

  private ProvisioningRequestResponse ToResponse(
    ProvisioningRequest x,
    IReadOnlyList<RequestLink> dependsOn,
    IReadOnlyList<RequestLink> dependents)
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
      x.ConversationId,
      x.CreatedAt,
      x.UpdatedAt,
      x.ConfirmedAt,
      dependsOn,
      dependents);
  }
}
