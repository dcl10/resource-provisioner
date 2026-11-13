namespace ResourceProvisioner.Api.Controllers;

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Services.Requests;

/// <summary>
/// Maps the request services' exceptions to problem details, for the controllers over requests and projects: 404 for
/// something the user can't see, 403 for an admin acting on someone else's, 409 for the wrong status or a limit
/// reached, 422 (with <c>errors</c> by field) for invalid variables.
/// </summary>
public abstract class RequestErrorsControllerBase : ControllerBase
{
  /// <summary>Runs an action, answering 200 with its result or a problem for the exception it throws.</summary>
  /// <typeparam name="T">The result's type.</typeparam>
  /// <param name="action">The service call.</param>
  /// <returns>The result, or a problem.</returns>
  protected Task<ActionResult<T>> Handle<T>(Func<Task<T>> action) =>
    HandleResult<T>(async () => await action());

  /// <summary>Runs an action that picks its own result (such as 201), answering a problem for what it throws.</summary>
  /// <typeparam name="T">The result's type.</typeparam>
  /// <param name="action">The service call and its result.</param>
  /// <returns>The result, or a problem.</returns>
  protected async Task<ActionResult<T>> HandleResult<T>(Func<Task<ActionResult<T>>> action)
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
