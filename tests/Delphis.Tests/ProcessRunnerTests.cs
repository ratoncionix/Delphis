using Delphis.Core.Services;

namespace Delphis.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public void RunAsync_FastCommand_ShouldDrainCompleteStdoutAndStderr()
    {
        if (OperatingSystem.IsWindows())
            return;

        var runner = new ProcessRunner();
        var result = runner.RunAsync(
            "/bin/sh",
            new[] { "-c", "printf 'line1\\nline2\\n'; printf 'err1\\n' >&2" }
        ).GetAwaiter().GetResult();

        Assert.True(result.IsSuccess);
        Assert.Equal("line1\nline2", result.StandardOutput.Replace("\r\n", "\n"));
        Assert.Equal("err1", result.StandardError.Replace("\r\n", "\n"));
    }

    [Fact]
    public void RunAsync_Cancelled_ShouldTerminateAndSetWasCancelled()
    {
        if (OperatingSystem.IsWindows())
            return;

        var runner = new ProcessRunner();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        var result = runner.RunAsync(
            "/bin/sh",
            new[] { "-c", "sleep 10" },
            cancellationToken: cts.Token
        ).GetAwaiter().GetResult();

        Assert.True(result.WasCancelled);
        Assert.False(result.IsSuccess);
    }
}
