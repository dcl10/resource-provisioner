namespace ResourceProvisioner.Api.Services.Requests;

using Data;
using Microsoft.EntityFrameworkCore;
using Models;

/// <summary>
/// The dependency graph between requests, both ways. A request depends on the requests it references
/// (<see cref="RequestDependency"/>) and on the <c>project</c> request backing the project it's in; both are counted,
/// so a member recorded without a dependency on its project's request is still found.
/// </summary>
public static class RequestGraph
{
  /// <summary>The IDs of the requests that depend directly on any of <paramref name="ids"/>.</summary>
  /// <param name="db">The app database.</param>
  /// <param name="ids">The requests depended on.</param>
  /// <param name="cancellationToken">Cancels the read.</param>
  /// <returns>Their direct dependents, without duplicates.</returns>
  public static async Task<List<Guid>> DependentIdsAsync(
    AppDbContext db,
    IReadOnlyCollection<Guid> ids,
    CancellationToken cancellationToken)
  {
    var referencing = await db.RequestDependencies.AsNoTracking()
      .Where(d => ids.Contains(d.DependsOnId))
      .Select(d => d.RequestId)
      .ToListAsync(cancellationToken);
    var members = await db.ProvisioningRequests.AsNoTracking()
      .Where(r => db.Projects.Any(p => p.Id == r.ProjectId && ids.Contains(p.ProjectRequestId)))
      .Select(r => r.Id)
      .ToListAsync(cancellationToken);
    return [.. referencing.Concat(members).Distinct()];
  }

  /// <summary>The IDs of the requests any of <paramref name="ids"/> depends on directly.</summary>
  /// <param name="db">The app database.</param>
  /// <param name="ids">The dependent requests.</param>
  /// <param name="cancellationToken">Cancels the read.</param>
  /// <returns>Their direct dependencies, without duplicates.</returns>
  public static async Task<List<Guid>> DependencyIdsAsync(
    AppDbContext db,
    IReadOnlyCollection<Guid> ids,
    CancellationToken cancellationToken)
  {
    var referenced = await db.RequestDependencies.AsNoTracking()
      .Where(d => ids.Contains(d.RequestId))
      .Select(d => d.DependsOnId)
      .ToListAsync(cancellationToken);
    var projectRequests = await db.ProvisioningRequests.AsNoTracking()
      .Where(r => ids.Contains(r.Id))
      .Join(db.Projects, r => r.ProjectId, p => (Guid?)p.Id, (_, p) => p.ProjectRequestId)
      .ToListAsync(cancellationToken);
    return [.. referenced.Concat(projectRequests).Distinct()];
  }

  /// <summary>
  /// Everything that depends on <paramref name="root"/>, directly or not, in teardown order: each request comes before
  /// anything in the set it depends on, newest first among equals. Loaded tracked, so the caller can change them.
  /// </summary>
  /// <param name="db">The app database.</param>
  /// <param name="root">The request at the bottom.</param>
  /// <param name="cancellationToken">Cancels the read.</param>
  /// <returns>Its transitive dependents, not including it.</returns>
  public static async Task<List<ProvisioningRequest>> DependentsInTeardownOrderAsync(
    AppDbContext db,
    ProvisioningRequest root,
    CancellationToken cancellationToken)
  {
    var seen = new HashSet<Guid> { root.Id };
    var frontier = new List<Guid> { root.Id };
    while (frontier.Count > 0)
    {
      frontier = [.. (await DependentIdsAsync(db, frontier, cancellationToken)).Where(seen.Add)];
    }

    seen.Remove(root.Id);
    var requests = await db.ProvisioningRequests
      .Where(r => seen.Contains(r.Id))
      .ToListAsync(cancellationToken);

    // Edges inside the set: each request with the ones in it that depend on it, by reference or project.
    var references = await db.RequestDependencies.AsNoTracking()
      .Where(d => seen.Contains(d.RequestId) && seen.Contains(d.DependsOnId))
      .Select(d => new { d.RequestId, d.DependsOnId })
      .ToListAsync(cancellationToken);
    var projectRequests = await db.Projects.AsNoTracking()
      .Where(p => seen.Contains(p.ProjectRequestId))
      .ToDictionaryAsync(p => p.Id, p => p.ProjectRequestId, cancellationToken);
    var edges = references.Select(d => (d.RequestId, d.DependsOnId))
      .Concat(requests
        .Where(r => r.ProjectId is { } id && projectRequests.ContainsKey(id))
        .Select(r => (RequestId: r.Id, DependsOnId: projectRequests[r.ProjectId!.Value])))
      .ToList();

    var ordered = new List<ProvisioningRequest>();
    var remaining = requests.OrderByDescending(r => r.CreatedAt).ThenBy(r => r.Id).ToList();
    while (remaining.Count > 0)
    {
      // References only point at earlier requests, so there's no cycle and always one with nothing left above it.
      var next = remaining.FirstOrDefault(r =>
                   edges.Where(e => e.DependsOnId == r.Id).All(e => ordered.Any(o => o.Id == e.RequestId))) ??
                 remaining[0];
      ordered.Add(next);
      remaining.Remove(next);
    }

    return ordered;
  }
}
