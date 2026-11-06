using System.Diagnostics;
using DockerPortMirror.Processes;

namespace DockerPortMirror.Tests.Processes;

public sealed class WindowsJobObjectTrackerTests
{
    [Fact]
    public void Closing_the_job_kills_tracked_processes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Job Objects exist only on Windows.");
        if (!OperatingSystem.IsWindows())
            return;

        using var process = Process.Start(new ProcessStartInfo("ping", ["-n", "60", "127.0.0.1"])
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        try
        {
            var tracker = new WindowsJobObjectTracker();
            tracker.Track(process);

            tracker.Dispose();

            Assert.True(process.WaitForExit(TimeSpan.FromSeconds(10)), "The tracked process was not killed.");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill();
        }
    }
}
