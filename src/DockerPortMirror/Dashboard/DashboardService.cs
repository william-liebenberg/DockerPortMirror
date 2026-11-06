using DockerPortMirror.Cli;
using DockerPortMirror.Mirroring;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DockerPortMirror.Dashboard;

/// <summary>
/// Shows the dashboard in a live region of the terminal and redraws it every 250 ms, which also counts down the
/// retries. When the host stops, the mirror has already stopped (it is registered after this service), so the
/// final frame shows its last lines; the frame stays in the terminal.
/// </summary>
internal sealed class DashboardService : BackgroundService
{
    internal static readonly TimeSpan RefreshInterval = TimeSpan.FromMilliseconds(250);

    private readonly MirrorStatus _status;
    private readonly DashboardLog _log;
    private readonly MirrorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IAnsiConsole _console;

    public DashboardService(MirrorStatus status, DashboardLog log, IOptions<MirrorOptions> options, TimeProvider timeProvider)
        : this(status, log, options, timeProvider, AnsiConsole.Console)
    {
    }

    /// <summary>Draws on <paramref name="console"/> instead of the terminal (for tests).</summary>
    internal DashboardService(MirrorStatus status, DashboardLog log, IOptions<MirrorOptions> options, TimeProvider timeProvider, IAnsiConsole console)
    {
        ArgumentNullException.ThrowIfNull(options);
        _status = status;
        _log = log;
        _options = options.Value;
        _timeProvider = timeProvider;
        _console = console;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting before taking over the terminal.
        await Task.Yield();
        await _console.Live(Render())
            .AutoClear(false)
            .Overflow(VerticalOverflow.Ellipsis)
            .Cropping(VerticalOverflowCropping.Bottom)
            .StartAsync(async context =>
            {
                using var timer = new PeriodicTimer(RefreshInterval, _timeProvider);
                try
                {
                    while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                        context.UpdateTarget(Render());
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    // Normal shutdown.
                }

                // The final frame, with the last lines of the mirror, which stopped before this service.
                context.UpdateTarget(Render());
            })
            .ConfigureAwait(false);
    }

    private IRenderable Render() =>
        DashboardView.Render(_options, _status.Snapshot(), _log.Lines(), _timeProvider.GetUtcNow(), _console.Profile);
}
