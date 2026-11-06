using DockerPortMirror.Cli;
using DockerPortMirror.Diagnostics;
using DockerPortMirror.Docker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DockerPortMirror.Mirroring;

/// <summary>Resolves the Docker context, then polls the published ports and reconciles the tunnels until stopped.</summary>
internal sealed class MirrorWorker : BackgroundService
{
    private readonly DockerContextResolver _resolver;
    private readonly IPublishedPortSource _source;
    private readonly TunnelReconciler _reconciler;
    private readonly MirrorStatus _status;
    private readonly MirrorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<MirrorWorker> _logger;
    private string? _lastPollError;

    public MirrorWorker(
        DockerContextResolver resolver,
        IPublishedPortSource source,
        TunnelReconciler reconciler,
        MirrorStatus status,
        IOptions<MirrorOptions> options,
        TimeProvider timeProvider,
        IHostApplicationLifetime lifetime,
        ILogger<MirrorWorker> logger)
    {
        _resolver = resolver;
        _source = source;
        _reconciler = reconciler;
        _status = status;
        _options = options.Value;
        _timeProvider = timeProvider;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <summary>The process exit code: 0 after a normal stop, 1 after a fatal error.</summary>
    public int ExitCode { get; private set; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the host finish starting before running the first (slow) Docker command.
        await Task.Yield();
        try
        {
            var endpoint = await _resolver.ResolveAsync(stoppingToken).ConfigureAwait(false);
            _status.SetEndpoint(endpoint);
            WriteBanner(endpoint);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollSeconds), _timeProvider);
            do
            {
                await PollOnceAsync(stoppingToken).ConfigureAwait(false);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (DockerPortMirrorException ex)
        {
            ExitCode = 1;
            Log.FatalError(_logger, ex.Message);
        }
#pragma warning disable CA1031 // The worker is the last line of defence: report, exit 1, and let the host stop.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ExitCode = 1;
            Log.UnexpectedError(_logger, ex.Message, _options.Verbose ? ex : null);
        }
        finally
        {
            if (_reconciler.OpenTunnels.Count > 0)
                Log.Stopping(_logger);
            await _reconciler.DisposeAsync().ConfigureAwait(false);
            _lifetime.StopApplication();
        }
    }

    internal async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var ports = await _source.GetPublishedPortsAsync(cancellationToken).ConfigureAwait(false);
            _status.SetDockerError(null);
            await _reconciler.ReconcileAsync(ports, cancellationToken).ConfigureAwait(false);
            if (_lastPollError is not null)
                Log.PollRecovered(_logger);
            _lastPollError = null;
        }
        catch (DockerPortMirrorException ex)
        {
            // Report a failure once per change, not every poll.
            if (!string.Equals(ex.Message, _lastPollError, StringComparison.Ordinal))
                Log.PollFailed(_logger, ex.Message);
            else
                Log.PollStillFailing(_logger, ex.Message);
            _lastPollError = ex.Message;
            _status.SetDockerError(ex.Message);
        }
    }

    private void WriteBanner(DockerEndpoint endpoint)
    {
        Log.Banner(_logger, AppInfo.Name, AppInfo.Version);
        Log.BannerContext(_logger, endpoint.Context, endpoint.Host);
        Log.BannerSshTarget(_logger, endpoint.SshTarget.Destination);
        Log.BannerSettings(_logger, _options.BindAddress, _options.PollSeconds);
        Log.BannerHint(_logger);
    }
}
