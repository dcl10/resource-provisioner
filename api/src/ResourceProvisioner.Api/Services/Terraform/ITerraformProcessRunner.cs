namespace ResourceProvisioner.Api.Services.Terraform;

using Models;

/// <summary>
/// Runs one Terraform CLI command. Behind an interface so tests never start a real process or reach DigitalOcean.
/// </summary>
public interface ITerraformProcessRunner
{
  /// <summary>
  /// Runs <c>terraform &lt;arguments&gt;</c> in <paramref name="workingDirectory"/> and waits for it to exit.
  /// <paramref name="onLine"/> is called for each line of output, one call at a time, in the order the lines arrived.
  /// </summary>
  /// <param name="workingDirectory">The request's workspace.</param>
  /// <param name="arguments">The arguments after <c>terraform</c>.</param>
  /// <param name="environment">Variables added to the process's environment, credentials among them.</param>
  /// <param name="onLine">Receives each output line and the stream it came from.</param>
  /// <param name="cancellationToken">Kills the process.</param>
  /// <returns>The exit code.</returns>
  Task<int> RunAsync(
    string workingDirectory,
    IReadOnlyList<string> arguments,
    IReadOnlyDictionary<string, string> environment,
    Func<LogSource, string, Task> onLine,
    CancellationToken cancellationToken);
}
