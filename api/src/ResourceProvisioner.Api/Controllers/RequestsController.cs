namespace ResourceProvisioner.Api.Controllers;

using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Models;
using Services.Requests;

/// <summary>
/// Provisioning requests. Users see and act on their own; admins see everyone's (with the owner shown) and can destroy
/// any of them, but confirm or cancel only their own. Requests are created by the agent's <c>propose_request</c> tool,
/// not here.
/// Errors come back as problem details: 404 for a request the user can't see, 403 for an admin acting on someone
/// else's, 409 for the wrong status or a limit reached, 422 (with <c>errors</c> by field) when the variables are no
/// longer valid at confirm.
/// </summary>
/// <param name="requests">The request service.</param>
/// <param name="logs">The request log service.</param>
/// <param name="currentUser">The signed-in user.</param>
[ApiController]
[Route("api/requests")]
public class RequestsController(
  IProvisioningRequestService requests,
  IRequestLogService logs,
  ICurrentUser currentUser) : ControllerBase
{
  private static readonly JsonSerializerOptions EventJson = new(JsonSerializerDefaults.Web);

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

  /// <summary>
  /// Tears down what the request created: queues <c>terraform destroy</c> for the runner. Allowed from
  /// <c>Succeeded</c> or <c>Failed</c>, by the owner or an admin; 409 from any other status. Follow it on
  /// <c>GET .../logs</c>.
  /// </summary>
  /// <param name="id">Request ID.</param>
  /// <param name="cancellationToken">Cancels the request.</param>
  /// <returns>The request, now Destroying.</returns>
  [HttpPost("{id:guid}/destroy")]
  public Task<ActionResult<ProvisioningRequestResponse>> Destroy(Guid id, CancellationToken cancellationToken) =>
    Handle(() => requests.DestroyAsync(id, Caller, cancellationToken));

  /// <summary>
  /// Streams the request's Terraform log as server-sent events: <c>status</c> (<c>{"status":"Applying"}</c>) first and
  /// on every change, <c>log</c> (<c>{"id","at","stream","text"}</c>, with the line's ID as the event ID) for each line,
  /// and a final <c>end</c> (<c>{"status"}</c>) once no job is queued or running. A reconnect with
  /// <c>Last-Event-ID</c> resumes after that line. Owner or admin; 404 otherwise.
  /// </summary>
  /// <param name="id">Request ID.</param>
  /// <param name="lastEventId">The last log line the client has, from the <c>Last-Event-ID</c> header.</param>
  /// <param name="cancellationToken">Cancels the request; closing the connection stops the stream.</param>
  /// <returns>The event stream, or a 404 problem.</returns>
  [HttpGet("{id:guid}/logs")]
  [Produces("text/event-stream")]
  public async Task<IResult> Logs(
    Guid id,
    [FromHeader(Name = "Last-Event-ID")] string? lastEventId,
    CancellationToken cancellationToken)
  {
    var afterId = long.TryParse(lastEventId, out var parsed) && parsed > 0 ? parsed : 0;
    try
    {
      var events = await logs.FollowAsync(id, Caller, afterId, cancellationToken);
      return TypedResults.ServerSentEvents(ToSse(events, cancellationToken));
    }
    catch (KeyNotFoundException ex)
    {
      return TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status404NotFound);
    }
  }

  private static async IAsyncEnumerable<SseItem<string>> ToSse(
    IAsyncEnumerable<RequestLogEvent> events,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    await foreach (var e in events.WithCancellation(cancellationToken))
    {
      yield return e.Kind switch
      {
        RequestLogEventKind.Log => new SseItem<string>(JsonSerializer.Serialize(e.Line, EventJson), "log")
        {
          EventId = e.Id?.ToString(System.Globalization.CultureInfo.InvariantCulture),
        },
        RequestLogEventKind.Status => new SseItem<string>(
          JsonSerializer.Serialize(new { status = e.Status }, EventJson), "status"),
        _ => new SseItem<string>(JsonSerializer.Serialize(new { status = e.Status }, EventJson), "end"),
      };
    }
  }

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
