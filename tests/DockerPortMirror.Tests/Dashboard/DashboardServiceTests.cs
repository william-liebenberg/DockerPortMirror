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
        Assert.Contains(firstBannerRow, LastFrame(console.Output, "Mirrored ports (0)"), StringComparison.Ordinal);

        await using (var reconciler = new TunnelReconciler(new FakeTunnelFactory(), TimeProvider.System, options, status, loggerFactory.CreateLogger<TunnelReconciler>()))
        {
            await reconciler.ReconcileAsync(new HashSet<PublishedPort> { new(IPAddress.Any, 5432, "postgres") }, token);
            await WaitUntilAsync(() => console.Output.Contains("localhost:5432", StringComparison.Ordinal), token);
            Assert.Contains(firstBannerRow, LastFrame(console.Output, "Mirrored ports (1)"), StringComparison.Ordinal);
            await reconciler.ReconcileAsync(new HashSet<PublishedPort>(), token);
        }

        // The host stops the mirror first: its last lines are in the log before the dashboard stops.
        log.Add(LogLevel.Information, 6, "Stopping tunnels...");
        await service.StopAsync(token);

        var finalFrame = LastFrame(console.Output, "Mirrored ports (0)");
        Assert.Contains(firstBannerRow, finalFrame, StringComparison.Ordinal);
        Assert.Contains(" - localhost:5432 (postgres)", finalFrame, StringComparison.Ordinal);
        Assert.Contains("Stopping tunnels...", finalFrame, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Short_live_frame_crops_the_bottom_not_the_banner()
    {
        var token = TestContext.Current.CancellationToken;
        var log = new DashboardLog(TimeProvider.System);
        log.Add(LogLevel.Information, 0, "This log is below the fold.");
        using var console = new TestConsole().Width(120).Height(8).Interactive().SupportsUnicode(true);
        using var service = new DashboardService(new MirrorStatus(), log, Options.Create(new MirrorOptions()), TimeProvider.System, console);
        using var figlet = new TestConsole().Width(120);
        figlet.Write(new FigletText(AppInfo.Name));
        var firstBannerRow = figlet.Lines[0].TrimEnd();

        await service.StartAsync(token);
        await WaitUntilAsync(() => console.Output.Contains(firstBannerRow, StringComparison.Ordinal), token);
        var liveOutput = console.Output;
        await service.StopAsync(token);
        Assert.DoesNotContain("This log is below the fold.", liveOutput, StringComparison.Ordinal);
    }

    private static string LastFrame(string output, string portsHeader)
    {
        var portsIndex = output.LastIndexOf("Mirrored ports", StringComparison.Ordinal);
        Assert.StartsWith(portsHeader, output[portsIndex..], StringComparison.Ordinal);
        var previousFrame = output.LastIndexOf("Mirrored ports", portsIndex - 1, StringComparison.Ordinal);
        return output[(previousFrame < 0 ? 0 : previousFrame + "Mirrored ports".Length)..];
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50, cancellationToken);
        Assert.True(condition());
    }
}
