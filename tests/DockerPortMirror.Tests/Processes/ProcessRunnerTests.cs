using System.Diagnostics;
using DockerPortMirror.Processes;
using Microsoft.Extensions.Logging.Abstractions;

namespace DockerPortMirror.Tests.Processes;

public sealed class ProcessRunnerTests
{
    private static readonly TimeSpan LongTimeout = TimeSpan.FromSeconds(30);

    private static ProcessRunner CreateRunner() => new(new NoOpChildProcessTracker(), NullLogger<ProcessRunner>.Instance);

    private static (string FileName, string[] Arguments) Echo(string text) =>
        OperatingSystem.IsWindows() ? ("cmd", ["/c", "echo " + text]) : ("sh", ["-c", "echo " + text]);

    private static (string FileName, string[] Arguments) Sleep(int seconds) =>
        OperatingSystem.IsWindows()
            ? ("ping", ["-n", (seconds + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), "127.0.0.1"])
            : ("sleep", [seconds.ToString(System.Globalization.CultureInfo.InvariantCulture)]);

    [Fact]
    public async Task Output_and_exit_code_are_returned()
    {
        var (fileName, arguments) = Echo("hello");

        var result = await CreateRunner().RunAsync(fileName, arguments, LongTimeout, TestContext.Current.CancellationToken);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("hello", result.StandardOutput.Trim());
    }

    [Fact]
    public async Task Missing_program_is_a_user_facing_error()
    {
        var error = await Assert.ThrowsAsync<DockerPortMirrorException>(() =>
            CreateRunner().RunAsync("dpm-no-such-program", [], LongTimeout, TestContext.Current.CancellationToken));

        Assert.Contains("dpm-no-such-program", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Process_is_stopped_when_the_timeout_passes()
    {
        var (fileName, arguments) = Sleep(30);
        var stopwatch = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<DockerPortMirrorException>(() =>
            CreateRunner().RunAsync(fileName, arguments, TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken));

        Assert.Contains("did not finish", error.Message, StringComparison.Ordinal);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Took {stopwatch.Elapsed}.");
    }

    [Fact]
    public async Task Process_is_stopped_when_cancelled()
    {
        var (fileName, arguments) = Sleep(30);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(300));
        var stopwatch = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CreateRunner().RunAsync(fileName, arguments, LongTimeout, cancellation.Token));

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Took {stopwatch.Elapsed}.");
    }
}
