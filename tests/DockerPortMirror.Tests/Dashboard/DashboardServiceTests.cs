using System.Net;
using DockerPortMirror.Cli;
using DockerPortMirror.Dashboard;
using DockerPortMirror.Diagnostics;
using DockerPortMirror.Mirroring;
using DockerPortMirror.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Testing;

namespace DockerPortMirror.Tests.Dashboard;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task Banner_is_shown_in_initial_refreshed_and_final_frames()
    {
        var token = TestContext.Current.CancellationToken;
        var options = Options.Create(new MirrorOptions());
        var status = new MirrorStatus();
        var log = new DashboardLog(TimeProvider.System);
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddMirrorDashboard(log, verbose: false));
        using var console = new TestConsole().Width(120).Height(30).Interactive().SupportsUnicode(true);
        using var service = new DashboardService(status, log, options, TimeProvider.System, console);
        using var figlet = new TestConsole().Width(120);
        figlet.Write(new FigletText(AppInfo.Name));
        var firstBannerRow = figlet.Lines[0].TrimEnd();

        await service.StartAsync(token);
        await WaitUntilAsync(() => console.Output.Contains("Waiting for Docker", StringComparison.Ordinal), token);
        Assert.Contains(firstBannerRow, LastFrame(console.Output, firstBannerRow), StringComparison.Ordinal);

        await using (var reconciler = new TunnelReconciler(new FakeTunnelFactory(), TimeProvider.System, options, status, loggerFactory.CreateLogger<TunnelReconciler>()))
        {
            await reconciler.ReconcileAsync(new HashSet<PublishedPort> { new(IPAddress.Any, 5432, "postgres") }, token);
            await WaitUntilAsync(() =>
            {
                var frame = LastFrame(console.Output, firstBannerRow);
                return frame.Contains("● open", StringComparison.Ordinal) && frame.Contains("➕ localhost:5432", StringComparison.Ordinal);
            }, token);
            AssertStackedPanels(LastFrame(console.Output, firstBannerRow), portCount: 1);
            await reconciler.ReconcileAsync(new HashSet<PublishedPort>(), token);
        }

        // The host stops the mirror first: its last lines are in the log before the dashboard stops.
        log.Add(LogLevel.Information, 6, "Stopping tunnels...");
        await service.StopAsync(token);

        var finalFrame = LastFrame(console.Output, firstBannerRow);
        Assert.StartsWith(firstBannerRow, finalFrame, StringComparison.Ordinal);
        AssertStackedPanels(finalFrame, portCount: 0);
        Assert.DoesNotContain("● open", finalFrame, StringComparison.Ordinal);
        Assert.Contains(" ➕ localhost:5432", finalFrame, StringComparison.Ordinal);
        Assert.Contains(" ➖ localhost:5432 (postgres)", finalFrame, StringComparison.Ordinal);
        Assert.Contains("Stopping tunnels...", finalFrame, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(79)]
    [InlineData(120)]
    public async Task Short_live_frame_crops_the_bottom_not_the_banner(int width)
    {
        var token = TestContext.Current.CancellationToken;
        var log = new DashboardLog(TimeProvider.System);
        log.Add(LogLevel.Information, 0, "This log is below the fold.");
        using var console = new TestConsole().Width(width).Height(8).Interactive().SupportsUnicode(true);
        using var service = new DashboardService(new MirrorStatus(), log, Options.Create(new MirrorOptions()), TimeProvider.System, console);
        using var figlet = new TestConsole().Width(120);
        figlet.Write(new FigletText(AppInfo.Name));
        var firstBannerRow = figlet.Lines[0][..Math.Min(width, figlet.Lines[0].Length)].TrimEnd();

        await service.StartAsync(token);
        await WaitUntilAsync(() => console.Output.Contains(firstBannerRow, StringComparison.Ordinal), token);
        var liveOutput = console.Output;
        await service.StopAsync(token);
        Assert.DoesNotContain("This log is below the fold.", liveOutput, StringComparison.Ordinal);
    }

    private static void AssertStackedPanels(string frame, int portCount)
    {
        var lines = frame.Split('\n');
        var connection = Array.FindIndex(lines, line => line.Contains(AppInfo.Name, StringComparison.Ordinal));
        var ports = Array.FindIndex(lines, line => line.Contains($"╭─Mirrored ports ({portCount})─", StringComparison.Ordinal));
        var log = Array.FindIndex(lines, line => line.Contains("╭─Log─", StringComparison.Ordinal));
        Assert.True(connection >= 0, frame);
        Assert.True(connection < ports && ports < log, frame);
    }

    private static string LastFrame(string output, string firstBannerRow)
    {
        var start = output.LastIndexOf(firstBannerRow, StringComparison.Ordinal);
        return start < 0 ? "" : output[start..];
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50, cancellationToken);
        Assert.True(condition());
    }
}
