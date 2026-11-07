namespace ResourceProvisioner.Api.Models;

using System.Text.Json.Serialization;

/// <summary>Where a job log line came from. Stored and serialized by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LogSource>))]
public enum LogSource
{
  /// <summary>The runner itself: the command it's starting, or why the job failed.</summary>
  System,

  /// <summary>Terraform's standard output.</summary>
  Stdout,

  /// <summary>Terraform's standard error.</summary>
  Stderr,
}
