namespace ResourceProvisioner.Api.Tests.Fixtures;

using System.Collections.Concurrent;
using Models;
using Services.Terraform;

/// <summary>
/// Stands in for the Terraform CLI: records each command and plays back scripted output and exit codes per
/// subcommand (<c>init</c>, <c>plan</c>, <c>apply</c>, <c>output</c>). Every command succeeds by default, and
/// <c>output</c> returns <see cref="DefaultOutputJson"/>.
/// </summary>
public class FakeTerraform : ITerraformProcessRunner
{
  /// <summary>What <c>terraform output -json</c> prints by default, for the fixture type <c>test-server</c>.</summary>
  public const string DefaultOutputJson = """
    {
      "id": { "sensitive": false, "type": "string", "value": "123" },
      "ipv4_address": { "sensitive": false, "type": "string", "value": "192.0.2.1" },
      "root_password": { "sensitive": true, "type": "string", "value": "output-secret" }
    }
    """;

  private readonly ConcurrentDictionary<string, Script> _scripts = new();

  /// <summary>Every command run, in order.</summary>
  public ConcurrentQueue<Invocation> Invocations { get; } = new();

  /// <summary>Sets what a subcommand prints and returns.</summary>
  /// <param name="subcommand">The first argument, such as <c>plan</c>.</param>
  /// <param name="exitCode">Its exit code.</param>
  /// <param name="lines">The lines it prints, in order.</param>
  public void Script(string subcommand, int exitCode, params (LogSource Source, string Text)[] lines) =>
    _scripts[subcommand] = new Script(exitCode, lines);

  /// <summary>Forgets scripts and invocations, so each test starts from the defaults.</summary>
  public void Reset()
  {
    _scripts.Clear();
    Invocations.Clear();
  }

  /// <inheritdoc />
  public async Task<int> RunAsync(
    string workingDirectory,
    IReadOnlyList<string> arguments,
    IReadOnlyDictionary<string, string> environment,
    Func<LogSource, string, Task> onLine,
    CancellationToken cancellationToken)
  {
    Invocations.Enqueue(new Invocation(workingDirectory, [.. arguments], new Dictionary<string, string>(environment)));

    var script = _scripts.GetValueOrDefault(arguments[0]) ?? Default(arguments[0]);
    foreach (var (source, text) in script.Lines)
    {
      await onLine(source, text);
    }

    return script.ExitCode;
  }

  private static Script Default(string subcommand) =>
    subcommand == "output"
      ? new Script(0, [.. DefaultOutputJson.Split('\n').Select(l => (LogSource.Stdout, l))])
      : new Script(0, [(LogSource.Stdout, $"{subcommand} ok")]);

  /// <summary>One command the runner ran.</summary>
  /// <param name="WorkingDirectory">Where it ran.</param>
  /// <param name="Arguments">Its arguments after <c>terraform</c>.</param>
  /// <param name="Environment">The variables it was given.</param>
  public record Invocation(
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    IReadOnlyDictionary<string, string> Environment);
}

internal sealed record Script(int ExitCode, (LogSource Source, string Text)[] Lines);
