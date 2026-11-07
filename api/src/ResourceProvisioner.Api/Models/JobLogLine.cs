namespace ResourceProvisioner.Api.Models;

/// <summary>
/// One line of a request's Terraform log, saved as it's written so the logs endpoint can stream it live and replay it
/// later. Never holds <c>terraform output</c>, which can include sensitive values.
/// </summary>
public class JobLogLine
{
  /// <summary>Increasing ID; lines are ordered by it, and the logs endpoint uses it as the SSE event ID.</summary>
  public long Id { get; set; }

  /// <summary>The request the line belongs to.</summary>
  public Guid RequestId { get; set; }

  /// <summary>When the line was written.</summary>
  public DateTimeOffset At { get; set; }

  /// <summary>Where it came from.</summary>
  public LogSource Stream { get; set; }

  /// <summary>The line, without its line break.</summary>
  public required string Text { get; set; }
}
