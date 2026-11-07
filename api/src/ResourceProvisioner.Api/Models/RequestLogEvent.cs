namespace ResourceProvisioner.Api.Models;

/// <summary>
/// One event on <c>GET /api/requests/{id}/logs</c>: a log line, the request's status, or the end of the stream.
/// </summary>
/// <param name="Kind">Which of the three it is; also the SSE event type.</param>
/// <param name="Id">The log line's ID, for <c>Last-Event-ID</c>; null for status and end events.</param>
/// <param name="Line">The log line, for a <see cref="RequestLogEventKind.Log"/> event.</param>
/// <param name="Status">The request's status, for status and end events.</param>
public record RequestLogEvent(RequestLogEventKind Kind, long? Id, JobLogLineResponse? Line, RequestStatus? Status);

/// <summary>The kinds of <see cref="RequestLogEvent"/>.</summary>
public enum RequestLogEventKind
{
  /// <summary>A log line (SSE event <c>log</c>).</summary>
  Log,

  /// <summary>The request's status, sent first and whenever it changes (SSE event <c>status</c>).</summary>
  Status,

  /// <summary>Nothing more is coming: no job is queued or running for the request (SSE event <c>end</c>).</summary>
  End,
}

/// <summary>A log line as the logs endpoint sends it.</summary>
/// <param name="Id">The line's ID.</param>
/// <param name="At">When it was written.</param>
/// <param name="Stream">Where it came from.</param>
/// <param name="Text">The line.</param>
public record JobLogLineResponse(long Id, DateTimeOffset At, LogSource Stream, string Text);
