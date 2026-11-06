using DockerPortMirror.Cli;
using DockerPortMirror.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DockerPortMirror.Mirroring;

/// <summary>Keeps one live tunnel for each published port, opening and closing tunnels as ports come and go.</summary>
internal sealed class TunnelReconciler : IAsyncDisposable
{
    private readonly ITunnelFactory _factory;
    private readonly RetryPolicy _retry;
    private readonly MirrorOptions _options;
    private readonly ILogger<TunnelReconciler> _logger;
    private readonly Dictionary<TunnelSpec, ITunnel> _tunnels = new();

    public TunnelReconciler(
        ITunnelFactory factory,
        TimeProvider timeProvider,
        IOptions<MirrorOptions> options,
        ILogger<TunnelReconciler> logger)
    {
        _factory = factory;
        _retry = new RetryPolicy(timeProvider);
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>The tunnels that are currently open.</summary>
    public IReadOnlyCollection<TunnelSpec> OpenTunnels => _tunnels.Keys;

    public async Task ReconcileAsync(IReadOnlySet<PublishedPort> publishedPorts, CancellationToken cancellationToken)
    {
        var desired = TunnelSpec.FromPublishedPorts(publishedPorts, _options.BindAddress);

        await RemoveExitedTunnelsAsync(desired).ConfigureAwait(false);

        // A tunnel still alive one poll after it opened is healthy: forget its past failures.
        foreach (var spec in _tunnels.Keys)
            _retry.Reset(spec);

        var diff = PortSetDiff.Compute(_tunnels.Keys, desired);
        foreach (var spec in diff.ToRemove.OrderBy(spec => spec.LocalPort))
        {
            await CloseAsync(spec).ConfigureAwait(false);
            Log.TunnelClosed(_logger, spec.LocalPort);
        }

        _retry.Retain(desired);

        foreach (var spec in diff.ToAdd.Where(_retry.IsDue).OrderBy(spec => spec.LocalPort))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await OpenAsync(spec, cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var spec in _tunnels.Keys.ToArray())
            await CloseAsync(spec).ConfigureAwait(false);
    }

    private async Task OpenAsync(TunnelSpec spec, CancellationToken cancellationToken)
    {
        try
        {
            var tunnel = await _factory.OpenAsync(spec, cancellationToken).ConfigureAwait(false);
            _tunnels[spec] = tunnel;
            Log.TunnelOpened(_logger, spec.LocalPort, tunnel.Via, spec.RemoteHost, spec.RemotePort);
        }
        catch (DockerPortMirrorException ex)
        {
            ReportFailure(spec, ex.Message);
        }
    }

    private async Task RemoveExitedTunnelsAsync(IReadOnlySet<TunnelSpec> desired)
    {
        foreach (var (spec, tunnel) in _tunnels.Where(pair => pair.Value.Completion.IsCompleted).ToArray())
        {
            var exit = await tunnel.Completion.ConfigureAwait(false);
            await CloseAsync(spec).ConfigureAwait(false);
            if (desired.Contains(spec))
                ReportFailure(spec, $"tunnel exited: {exit.Reason}");
            else
                Log.TunnelClosed(_logger, spec.LocalPort);
        }
    }

    private void ReportFailure(TunnelSpec spec, string reason)
    {
        var decision = _retry.Fail(spec, reason);
        var retrySeconds = (int)Math.Ceiling(decision.Delay.TotalSeconds);
        if (decision.IsNewState)
            Log.TunnelFailed(_logger, spec.LocalPort, reason, retrySeconds);
        else
            Log.TunnelStillFailing(_logger, spec.LocalPort, decision.Failures, reason, retrySeconds);
    }

    private async Task CloseAsync(TunnelSpec spec)
    {
        if (_tunnels.Remove(spec, out var tunnel))
            await tunnel.DisposeAsync().ConfigureAwait(false);
    }
}
