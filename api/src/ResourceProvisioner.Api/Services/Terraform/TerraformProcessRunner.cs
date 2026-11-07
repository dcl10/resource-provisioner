namespace ResourceProvisioner.Api.Services.Terraform;

using System.Diagnostics;
using System.Threading.Channels;
using Config;
using Microsoft.Extensions.Options;
using Models;

/// <summary><see cref="ITerraformProcessRunner"/> that starts the CLI at <c>Runner:TerraformPath</c>.</summary>
/// <param name="options">Where the CLI is.</param>
public class TerraformProcessRunner(IOptions<RunnerOptions> options) : ITerraformProcessRunner
{
  /// <inheritdoc />
  public async Task<int> RunAsync(
    string workingDirectory,
    IReadOnlyList<string> arguments,
    IReadOnlyDictionary<string, string> environment,
    Func<LogSource, string, Task> onLine,
    CancellationToken cancellationToken)
  {
    var startInfo = new ProcessStartInfo(options.Value.TerraformPath)
    {
      WorkingDirectory = workingDirectory,
      RedirectStandardOutput = true,
      RedirectStandardError = true,
      UseShellExecute = false,
    };
    foreach (var argument in arguments)
    {
      startInfo.ArgumentList.Add(argument);
    }

    foreach (var (name, value) in environment)
    {
      startInfo.Environment[name] = value;
    }

    // stdout and stderr arrive on different threads; funnel them through one channel so onLine is never called
    // concurrently (it writes to a DbContext).
    var lines = Channel.CreateUnbounded<(LogSource Source, string Text)>(
      new UnboundedChannelOptions { SingleReader = true });

    using var process = new Process { StartInfo = startInfo };
    process.Start();

    var stdout = Pump(process.StandardOutput, LogSource.Stdout, lines.Writer, cancellationToken);
    var stderr = Pump(process.StandardError, LogSource.Stderr, lines.Writer, cancellationToken);
    var pumps = Task.WhenAll(stdout, stderr).ContinueWith(
      t => lines.Writer.TryComplete(t.Exception),
      CancellationToken.None,
      TaskContinuationOptions.ExecuteSynchronously,
      TaskScheduler.Default);

    try
    {
      await foreach (var (stream, text) in lines.Reader.ReadAllAsync(cancellationToken))
      {
        await onLine(stream, text);
      }

      await process.WaitForExitAsync(cancellationToken);
    }
    catch (OperationCanceledException)
    {
      process.Kill(entireProcessTree: true);
      throw;
    }

    await pumps;
    return process.ExitCode;
  }

  private static async Task Pump(
    StreamReader reader,
    LogSource stream,
    ChannelWriter<(LogSource, string)> writer,
    CancellationToken cancellationToken)
  {
    while (await reader.ReadLineAsync(cancellationToken) is { } line)
    {
      await writer.WriteAsync((stream, line), cancellationToken);
    }
  }
}
