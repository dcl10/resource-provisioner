namespace ResourceProvisioner.Api.Controllers;

using Auth;
using Microsoft.AspNetCore.Mvc;
using Models;
using Services.Projects;
using Services.Requests;

/// <summary>
/// Projects, the top level that holds a user's resources. Users see their own; admins see everyone's (with the owner
/// shown). Anyone else's answers 404. Creating one proposes and confirms its <c>project</c> request in one step, since
/// the form's submit is the user's confirmation; the agent proposes them through <c>propose_request</c> instead.
/// Errors come back as problem details, as for requests.
/// </summary>
/// <param name="projects">The project service.</param>
/// <param name="currentUser">The signed-in user.</param>
[ApiController]
[Route("api/projects")]
public class ProjectsController(IProjectService projects, ICurrentUser currentUser) : RequestErrorsControllerBase
{
  private RequestUser Caller => RequestUser.From(currentUser);

  /// <summary>Lists the user's projects, or every user's for an admin, newest first.</summary>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The projects, with their status and resource counts.</returns>
  [HttpGet]
  public Task<ActionResult<IReadOnlyList<ProjectResponse>>> List(CancellationToken cancellationToken) =>
    Handle(() => projects.ListAsync(Caller, cancellationToken));

  /// <summary>Reads one project, with its <c>project</c> request and the requests in it.</summary>
  /// <param name="id">Project ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The project.</returns>
  [HttpGet("{id:guid}")]
  public Task<ActionResult<ProjectDetailResponse>> Get(Guid id, CancellationToken cancellationToken) =>
    Handle(() => projects.GetAsync(id, Caller, cancellationToken));

  /// <summary>
  /// Creates a project from the form: proposes its <c>project</c> request and confirms it, queueing it for the
  /// runner. 422 with <c>errors</c> by field when the variables aren't valid; 409 when a limit is reached.
  /// </summary>
  /// <param name="body">The <c>project</c> request's variables.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>201 with the new project.</returns>
  [HttpPost]
  public Task<ActionResult<ProjectDetailResponse>> Create(
    CreateProjectRequest body,
    CancellationToken cancellationToken) =>
    HandleResult<ProjectDetailResponse>(async () =>
    {
      var project = await projects.CreateAsync(Caller, body.Variables, cancellationToken);
      return CreatedAtAction(nameof(Get), new { id = project.Id }, project);
    });

  /// <summary>
  /// Deletes a project: a cascading destroy of its <c>project</c> request, which everything in it depends on, so every
  /// resource in it is destroyed first and every proposal in it cancelled. Once the runner has destroyed the project's
  /// request, the project is gone. Owner or admin; 404 otherwise; 409 as for destroying a request. The plan is
  /// <c>GET /api/requests/{projectRequestId}/destroy-plan</c>.
  /// </summary>
  /// <param name="id">Project ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>202 with the project, now Destroying.</returns>
  [HttpDelete("{id:guid}")]
  public Task<ActionResult<ProjectDetailResponse>> Delete(Guid id, CancellationToken cancellationToken) =>
    HandleResult<ProjectDetailResponse>(async () =>
      Accepted(await projects.DeleteAsync(id, Caller, cancellationToken)));
}
