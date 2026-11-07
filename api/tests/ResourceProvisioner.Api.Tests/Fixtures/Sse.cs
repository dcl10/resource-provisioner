namespace ResourceProvisioner.Api.Tests.Fixtures;

using System.Text.Json.Nodes;

/// <summary>Parses a complete server-sent events body, as the API's SSE endpoints write it.</summary>
public static class Sse
{
  /// <summary>Splits a body into its events.</summary>
  /// <param name="body">The whole response body.</param>
  /// <returns>The events in order.</returns>
  public static List<SseEvent> Parse(string body) =>
  [
    .. body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
      .Select(block => block.Split('\n'))
      .Select(fields => new SseEvent(
        Field(fields, "event") ?? "message",
        Field(fields, "id"),
        JsonNode.Parse(Field(fields, "data")!)!)),
  ];

  private static string? Field(string[] fields, string name) =>
    fields.FirstOrDefault(f => f.StartsWith($"{name}: ", StringComparison.Ordinal))?[(name.Length + 2)..];
}

/// <summary>One server-sent event.</summary>
/// <param name="Type">The event type (<c>event:</c>).</param>
/// <param name="Id">The event ID, if any.</param>
/// <param name="Data">The JSON data.</param>
public sealed record SseEvent(string Type, string? Id, JsonNode Data);
