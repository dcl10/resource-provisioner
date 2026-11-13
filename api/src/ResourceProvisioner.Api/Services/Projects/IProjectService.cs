namespace ResourceProvisioner.Api.Services.Projects;

using System.Text.Json.Nodes;
using Models;
using Requests;

/// <summary>
/// Projects: listing and reading them, and creating one from a form. A project's status is its <c>project</c>
/// request's, and once that request is cancelled or destroyed the project is deleted with it. Ownership matches
/// requests': users see their own projects, admins see everyone's, and anyone else's answers as missing.
/// </summary>
public interface IProjectService
{
  /// <summary>Lists the user's projects, or every user's for an admin, newest first.</summary>
  /// <param name="user">The signed-in user.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The projects.</returns>
  Task<IReadOnlyList<ProjectResponse>> ListAsync(RequestUser user, CancellationToken cancellationToken = default);

  /// <summary>Reads one project with its request and the requests in it: the user's own, or anyone's for an admin.</summary>
  /// <param name="id">Project ID.</param>
  /// <param name="user">The signed-in user.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The project.</returns>
  /// <exception cref="KeyNotFoundException">
  /// No such project (its request may have been cancelled or destroyed), or it isn't the user's and they aren't an
  /// admin.
  /// </exception>
  Task<ProjectDetailResponse> GetAsync(Guid id, RequestUser user, CancellationToken cancellationToken = default);

  /// <summary>
  /// Creates a project from the UI's form: proposes a <c>project</c> request for the user and confirms it straight
  /// away, since the user's submit is the confirmation. The variables are checked against the <c>project</c>
  /// manifest, and the confirm makes the same checks as any other, spend caps included. If the confirm is refused,
  /// nothing is kept.
  /// </summary>
  /// <param name="user">The signed-in user; becomes the owner, admins included.</param>
  /// <param name="variables">The <c>project</c> request's variables.</param>
  /// <param name="cancellationToken">Cancels the call.</param>
  /// <returns>The new project, its request queued for the runner.</returns>
  /// <exception cref="RequestValidationException">The variables aren't valid.</exception>
  /// <exception cref="RequestLimitException">Confirming would go over a limit.</exception>
  Task<ProjectDetailResponse> CreateAsync(
    RequestUser user,
    JsonObject variables,
    CancellationToken cancellationToken = default);
}
