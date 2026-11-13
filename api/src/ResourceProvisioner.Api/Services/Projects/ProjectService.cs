namespace ResourceProvisioner.Api.Services.Projects;

using System.Text.Json.Nodes;
using Data;
using Microsoft.EntityFrameworkCore;
using Models;
using Requests;

/// <summary>
/// <see cref="IProjectService"/> on the app database. Proposing and confirming go through
/// <see cref="IProvisioningRequestService"/>, so a project gets exactly the checks any other request does.
/// </summary>
/// <param name="db">The app database.</param>
/// <param name="requests">The request service.</param>
public class ProjectService(AppDbContext db, IProvisioningRequestService requests) : IProjectService
{
  /// <summary>
  /// Statuses of a <c>project</c> request whose project is gone; also the statuses of requests a project's
  /// <see cref="ProjectResponse.ResourceCount"/> leaves out.
  /// </summary>
  public static readonly IReadOnlyList<RequestStatus> GoneStatuses = [RequestStatus.Cancelled, RequestStatus.Destroyed];

  /// <inheritdoc />
  public async Task<IReadOnlyList<ProjectResponse>> ListAsync(
    RequestUser user,
    CancellationToken cancellationToken = default)
  {
    var rows = await Visible(user)
      .OrderByDescending(x => x.Project.CreatedAt)
      .ToListAsync(cancellationToken);

    return [.. rows.Select(x => ToResponse(x.Project, x.Status, x.ResourceCount))];
  }

  /// <inheritdoc />
  public async Task<ProjectDetailResponse> GetAsync(
    Guid id,
    RequestUser user,
    CancellationToken cancellationToken = default)
  {
    var row = await Visible(user).SingleOrDefaultAsync(x => x.Project.Id == id, cancellationToken) ??
              throw new KeyNotFoundException($"Project {id} doesn't exist.");
    var project = row.Project;

    var request = await requests.GetAsync(project.ProjectRequestId, user, cancellationToken);
    var resources = (await requests.ListAsync(user, project.Id, cancellationToken))
      .Where(r => r.Id != project.ProjectRequestId)
      .ToList();

    return new ProjectDetailResponse(
      project.Id,
      project.OwnerId,
      project.OwnerEmail,
      project.Name,
      project.Description,
      project.Purpose,
      project.Environment,
      request.Status,
      project.ProjectRequestId,
      resources.Count(r => !GoneStatuses.Contains(r.Status)),
      project.CreatedAt,
      request,
      resources);
  }

  /// <inheritdoc />
  public async Task<ProjectDetailResponse> CreateAsync(
    RequestUser user,
    JsonObject variables,
    CancellationToken cancellationToken = default)
  {
    var proposed = await requests.ProposeAsync(
      user,
      Project.RequestType,
      variables,
      projectId: null,
      conversationId: null,
      cancellationToken);
    if (!proposed.IsValid)
    {
      throw new RequestValidationException(proposed.Errors);
    }

    var request = proposed.Request!;
    try
    {
      await requests.ConfirmAsync(request.Id, user, cancellationToken);
    }
    catch (Exception ex) when (ex is RequestLimitException or RequestConflictException or RequestValidationException)
    {
      // The form's submit was the confirmation; a project it couldn't confirm isn't left awaiting one. Deleting the
      // request deletes its project too.
      await db.ProvisioningRequests.Where(x => x.Id == request.Id).ExecuteDeleteAsync(CancellationToken.None);
      throw;
    }

    return await GetAsync(request.Project!.Id, user, cancellationToken);
  }

  /// <summary>
  /// The projects the user may see that aren't gone, with their request's status and how many requests are in them.
  /// </summary>
  private IQueryable<ProjectRow> Visible(RequestUser user) =>
    from project in db.Projects.AsNoTracking()
    join request in db.ProvisioningRequests on project.ProjectRequestId equals request.Id
    where (user.IsAdmin || project.OwnerId == user.UserId) && !GoneStatuses.Contains(request.Status)
    select new ProjectRow
    {
      Project = project,
      Status = request.Status,
      ResourceCount = db.ProvisioningRequests.Count(m => m.ProjectId == project.Id && !GoneStatuses.Contains(m.Status)),
    };

  private static ProjectResponse ToResponse(Project project, RequestStatus status, int resourceCount) =>
    new(
      project.Id,
      project.OwnerId,
      project.OwnerEmail,
      project.Name,
      project.Description,
      project.Purpose,
      project.Environment,
      status,
      project.ProjectRequestId,
      resourceCount,
      project.CreatedAt);

  // Initialised by members, not a constructor, so EF Core can still filter and sort on it.
  private sealed class ProjectRow
  {
    public required Project Project { get; init; }

    public RequestStatus Status { get; init; }

    public int ResourceCount { get; init; }
  }
}
