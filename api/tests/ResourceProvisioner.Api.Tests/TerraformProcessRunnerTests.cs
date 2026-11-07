namespace ResourceProvisioner.Api.Tests;

using Config;
using Microsoft.Extensions.Options;
using Models;
using Services.Terraform;
using Xunit;

/// <summary>
/// The real process wrapper, run against <c>/bin/sh</c> instead of Terraform (the "arguments" are a shell script), so
/// it needs no Terraform install. Skipped on Windows.
/// </summary>
public class TerraformProcessRunnerTests
{
  private readonly TerraformProcessRunner _runner =
    new(Options.Create(new RunnerOptions { TerraformPath = "/bin/sh" }));

  [Fact]
  public async Task RunAsync_ReportsLinesFromBothStreams_AndExitCode()
  {
    Assert.SkipWhen(OperatingSystem.IsWindows(), "Needs /bin/sh.");
    var lines = new List<(LogSource, string)>();

    var exitCode = await _runner.RunAsync(
      Path.GetTempPath(),
      ["-c", "echo one; echo two >&2; echo three; exit 3"],
      new Dictionary<string, string>(),
      (source, text) =>
      {
        lines.Add((source, text));
        return Task.CompletedTask;
      },
      CancellationToken.None);

    Assert.Equal(3, exitCode);
    Assert.Equal(
      [(LogSource.Stdout, "one"), (LogSource.Stdout, "three")],
      lines.Where(l => l.Item1 == LogSource.Stdout));
    Assert.Contains((LogSource.Stderr, "two"), lines);
  }

  [Fact]
  public async Task RunAsync_PassesEnvironmentAndWorkingDirectory()
  {
    Assert.SkipWhen(OperatingSystem.IsWindows(), "Needs /bin/sh.");
    var dir = Directory.CreateTempSubdirectory("rp-proc-").FullName;
    var lines = new List<string>();

    try
    {
      await _runner.RunAsync(
        dir,
        ["-c", "echo \"$RP_TEST_VALUE\"; basename \"$PWD\""],
        new Dictionary<string, string> { ["RP_TEST_VALUE"] = "from-env" },
        (_, text) =>
        {
          lines.Add(text);
          return Task.CompletedTask;
        },
        CancellationToken.None);
    }
    finally
    {
      Directory.Delete(dir, recursive: true);
    }

    Assert.Equal("from-env", lines[0]);
    Assert.Equal(Path.GetFileName(dir), lines[1]);
  }
}
