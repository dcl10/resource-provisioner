namespace ResourceProvisioner.Api.Controllers;

using Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Models;
using Services.Requests;

/// <summary>
/// Provisioning requests. Users see and act on their own; admins see everyone's (with the owner shown) but can
/// confirm or cancel only their own. Requests are created by the agent's <c>propose_request</c> tool, not here.
/// Errors come back as problem details: 404 for a request the user can't see, 403 for an admin acting on someone
/// else's, 409 for the wrong status or a limit reached, 422 (with <c>errors</c> by field) when the variables are no
/// longer valid at confirm.
/// </summary>
/// <param name="requests">The request service.</param>
/// <param name="currentUser">The signed-in user.</param>
[ApiController]
[Route("api/requests")]
public class RequestsController(IProvisioningRequestService requests, ICurrentUser currentUser) : ControllerBase
{
  private RequestUser Caller => RequestUser.From(currentUser);

  /// <summary>Lists the user's requests, or every request for an admin, newest first.</summary>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The requests.</returns>
  [HttpGet]
  public Task<ActionResult<IReadOnlyList<ProvisioningRequestResponse>>> List(CancellationToken cancellationToken) =>
    Handle(() => requests.ListAsync(Caller, cancellationToken));

  /// <summary>Reads one request.</summary>
  /// <param name="id">Request ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The request.</returns>
  [HttpGet("{id:guid}")]
  public Task<ActionResult<ProvisioningRequestResponse>> Get(Guid id, CancellationToken cancellationToken) =>
    Handle(() => requests.GetAsync(id, Caller, cancellationToken));

  /// <summary>
  /// The owner's Confirm click, the only thing that starts provisioning: queues the request for the runner, within
  /// the active-request limits.
  /// </summary>
  /// <param name="id">Request ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The queued request.</returns>
  [HttpPost("{id:guid}/confirm")]
  public Task<ActionResult<ProvisioningRequestResponse>> Confirm(Guid id, CancellationToken cancellationToken) =>
    Handle(() => requests.ConfirmAsync(id, Caller, cancellationToken));

  /// <summary>The owner's cancel, before confirming.</summary>
  /// <param name="id">Request ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The cancelled request.</returns>
  [HttpPost("{id:guid}/cancel")]
  public Task<ActionResult<ProvisioningRequestResponse>> Cancel(Guid id, CancellationToken cancellationToken) =>
    Handle(() => requests.CancelAsync(id, Caller, cancellationToken));

  /// <summary>Maps the service's exceptions to problem details.</summary>
  private async Task<ActionResult<T>> Handle<T>(Func<Task<T>> action)
  {
    try
    {
      return await action();
    }
    catch (KeyNotFoundException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
    catch (RequestForbiddenException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status403Forbidden);
    }
    catch (RequestConflictException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
    }
    catch (RequestLimitException ex)
    {
      return Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "Active request limit reached");
    }
    catch (RequestValidationException ex)
    {
      var modelState = new ModelStateDictionary();
      foreach (var (field, messages) in ex.Errors)
      {
        foreach (var message in messages)
        {
          modelState.AddModelError(field, message);
        }
      }

      return ValidationProblem(ex.Message, statusCode: StatusCodes.Status422UnprocessableEntity,
        modelStateDictionary: modelState);
    }
  }
}
