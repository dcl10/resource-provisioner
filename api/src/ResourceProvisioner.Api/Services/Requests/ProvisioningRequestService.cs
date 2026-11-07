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
/// the checks a manifest can't express: that droplet references name the owner's own provisioned droplets.
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
  /// <summary>The catalog type a droplet reference must point at.</summary>
  public const string DropletType = "droplet";

  // Counting active requests and queueing one must not interleave, or two confirms could both pass the limit. The
  // POC runs one API instance, so a process-wide lock is enough.
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
    var (values, errors) = await ValidateAsync(owner.UserId, type, variables, cancellationToken);
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
    await db.SaveChangesAsync(cancellationToken);

    return new ProposeResult(ToResponse(request), errors);
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

    // SQLite (tests) can't order by DateTimeOffset, so sort in memory; active requests are capped, so lists are small.
    var requests = await query.ToListAsync(cancellationToken);
    return [.. requests.OrderByDescending(x => x.CreatedAt).Select(ToResponse)];
  }

  /// <inheritdoc />
  public async Task<ProvisioningRequestResponse> GetAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default) =>
    ToResponse(await FindVisible(id, user, cancellationToken));

  /// <inheritdoc />
  public async Task<ProvisioningRequestResponse> ConfirmAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default)
  {
    var request = await FindOwn(id, user, "confirm", cancellationToken);
    EnsureCanMove(request, RequestStatus.Queued, "confirmed");

    // The catalog may have changed since it was proposed, and a referenced droplet may have been destroyed.
    var (_, errors) = await ValidateAsync(
      request.OwnerId,
      request.Type,
      JsonNode.Parse(request.Variables)?.AsObject(),
      cancellationToken);
    if (errors.Count > 0)
    {
      throw new RequestValidationException(errors);
    }

    await ConfirmLock.WaitAsync(cancellationToken);
    try
    {
      await EnforceLimits(request.OwnerId, cancellationToken);
      RequestStateMachine.Move(request, RequestStatus.Queued, time.GetUtcNow());
      await Save(request, cancellationToken);
    }
    finally
    {
      ConfirmLock.Release();
    }

    // Saved first, so if the API stops before the runner gets to it, the runner re-queues it at startup.
    await jobs.EnqueueAsync(request.Id, cancellationToken);
    return ToResponse(request);
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
    await Save(request, cancellationToken);

    return ToResponse(request);
  }

  /// <inheritdoc />
  public async Task<ProvisioningRequestResponse> DestroyAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default)
  {
    var request = await FindVisible(id, user, cancellationToken);
    EnsureCanMove(request, RequestStatus.Destroying, "destroyed");

    RequestStateMachine.Move(request, RequestStatus.Destroying, time.GetUtcNow());
    await Save(request, cancellationToken);

    // Saved first: if the API stops before the runner gets to it, the runner re-queues it at startup.
    await jobs.EnqueueAsync(request.Id, cancellationToken);
    return ToResponse(request);
  }

  /// <summary>
  /// The manifest's checks, then the droplet references in the values that passed them. Returns the values with
  /// defaults applied and every error by field path.
  /// </summary>
  private async Task<(JsonObject Values, Dictionary<string, string[]> Errors)> ValidateAsync(
    string ownerId,
    string type,
    JsonObject? variables,
    CancellationToken cancellationToken)
  {
    var result = catalog.ValidateVariables(type, variables);
    var errors = result.Errors.ToDictionary(e => e.Key, e => e.Value);

    if (catalog.Find(type) is { } manifest)
    {
      foreach (var (path, message) in await CheckDropletReferences(manifest, result, ownerId, cancellationToken))
      {
        errors[path] = [.. errors.GetValueOrDefault(path, []), message];
      }
    }

    return (result.Values, errors);
  }

  /// <summary>
  /// Every ID in a droplet-reference variable (<c>droplet_request_ids</c>, <c>*_droplet_request_ids</c>) must be a
  /// <c>droplet</c> request the owner owns that has succeeded. Someone else's request and a missing one get the same
  /// message, so the check doesn't reveal other users' request IDs.
  /// </summary>
  private async Task<List<(string Path, string Message)>> CheckDropletReferences(
    ResourceManifest manifest,
    VariableValidationResult result,
    string ownerId,
    CancellationToken cancellationToken)
  {
    var references = new List<(string Path, string Value)>();
    foreach (var variable in manifest.Variables.Where(v => ManifestChecker.IsDropletReference(v.Name)))
    {
      // A variable with its own errors (wrong shape, bad pattern) is already reported.
      if (result.Errors.Keys.Any(k => k == variable.Name || k.StartsWith($"{variable.Name}[", StringComparison.Ordinal)) ||
          result.Values[variable.Name] is not JsonArray items)
      {
        continue;
      }

      for (var i = 0; i < items.Count; i++)
      {
        references.Add(($"{variable.Name}[{i}]", items[i]?.GetValue<string>() ?? ""));
      }
    }

    if (references.Count == 0)
    {
      return [];
    }

    var ids = references.Select(r => Guid.TryParse(r.Value, out var id) ? id : Guid.Empty).Distinct().ToList();
    var droplets = await db.ProvisioningRequests.AsNoTracking()
      .Where(x => ids.Contains(x.Id) && x.OwnerId == ownerId && x.Type == DropletType)
      .ToDictionaryAsync(x => x.Id, x => x.Status, cancellationToken);

    var problems = new List<(string Path, string Message)>();
    foreach (var (path, value) in references)
    {
      if (!Guid.TryParse(value, out var id) || !droplets.TryGetValue(id, out var status))
      {
        problems.Add((path, $"'{value}' isn't the request ID of one of your droplets."));
      }
      else if (status != RequestStatus.Succeeded)
      {
        problems.Add((path,
          $"Droplet request {value} is {status}; only provisioned (Succeeded) droplets can be referenced."));
      }
    }

    return problems;
  }

  private async Task EnforceLimits(string ownerId, CancellationToken cancellationToken)
  {
    var active = db.ProvisioningRequests.Where(x => RequestStateMachine.ActiveStatuses.Contains(x.Status));

    var mine = await active.CountAsync(x => x.OwnerId == ownerId, cancellationToken);
    if (mine >= _limits.MaxActiveRequestsPerUser)
    {
      throw new RequestLimitException(
        $"You already have {mine} active requests, and each user can have at most " +
        $"{_limits.MaxActiveRequestsPerUser}. Destroy one before confirming another.");
    }

    var total = await active.CountAsync(cancellationToken);
    if (total >= _limits.MaxActiveRequestsTotal)
    {
      throw new RequestLimitException(
        $"The app already has {total} active requests across all users, its limit of " +
        $"{_limits.MaxActiveRequestsTotal}. Try again once some have been destroyed.");
    }
  }

  private static void EnsureCanMove(ProvisioningRequest request, RequestStatus to, string verb)
  {
    if (!RequestStateMachine.CanMove(request.Status, to))
    {
      throw new RequestConflictException($"This request is {request.Status}, so it can't be {verb}.");
    }
  }

  private async Task Save(ProvisioningRequest request, CancellationToken cancellationToken)
  {
    try
    {
      await db.SaveChangesAsync(cancellationToken);
    }
    catch (DbUpdateConcurrencyException)
    {
      throw new RequestConflictException(
        $"Request {request.Id} changed while this was being done. Reload it and try again.");
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

  private ProvisioningRequestResponse ToResponse(ProvisioningRequest x)
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
      x.ConfirmedAt);
  }
}
