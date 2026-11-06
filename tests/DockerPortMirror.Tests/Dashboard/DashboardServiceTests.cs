using System.Net;
using DockerPortMirror.Cli;
using DockerPortMirror.Dashboard;
using DockerPortMirror.Mirroring;
using DockerPortMirror.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console.Testing;

namespace DockerPortMirror.Tests.Dashboard;

public sealed class DashboardServiceTests
{
    [Fact]
    public async Task Live_frame_shows_the_status_and_the_final_frame_shows_the_shutdown()
    {
        var token = TestContext.Current.CancellationToken;
        var options = Options.Create(new MirrorOptions());
        var status = new MirrorStatus();
        var log = new DashboardLog(TimeProvider.System);
        using var loggerFactory = LoggerFactory.Create(logging => logging.AddMirrorDashboard(log, verbose: false));
        using var console = new TestConsole().Width(100).Height(30).Interactive().SupportsUnicode(true);
        using var service = new DashboardService(status, log, options, TimeProvider.System, console);

        await service.StartAsync(token);
        await using (var reconciler = new TunnelReconciler(new FakeTunnelFactory(), TimeProvider.System, options, status, loggerFactory.CreateLogger<TunnelReconciler>()))
        {
            await reconciler.ReconcileAsync(new HashSet<PublishedPort> { new(IPAddress.Any, 5432, "postgres") }, token);
            await WaitUntilAsync(() => console.Output.Contains("localhost:5432", StringComparison.Ordinal), token);
            await reconciler.ReconcileAsync(new HashSet<PublishedPort>(), token);
        }

        // The host stops the mirror first: its last lines are in the log before the dashboard stops.
        log.Add(LogLevel.Information, 6, "Stopping tunnels...");
        await service.StopAsync(token);

        var output = console.Output;
        var finalFrame = output[output.LastIndexOf("Mirrored ports", StringComparison.Ordinal)..];
        Assert.Contains("Mirrored ports (0)", finalFrame, StringComparison.Ordinal);
        Assert.Contains(" - localhost:5432 (postgres)", finalFrame, StringComparison.Ordinal);
        Assert.Contains("Stopping tunnels...", finalFrame, StringComparison.Ordinal);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(50, cancellationToken);
        Assert.True(condition());
    }
}
