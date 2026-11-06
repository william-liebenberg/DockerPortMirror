using DockerPortMirror.Cli;
using DockerPortMirror.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DockerPortMirror.Mirroring;

/// <summary>
/// Keeps one live tunnel for each published port, opening and closing tunnels as ports come and go, and publishes
/// the state of every port to <see cref="MirrorStatus"/>.
/// </summary>
internal sealed class TunnelReconciler : IAsyncDisposable
{
    private readonly ITunnelFactory _factory;
    private readonly RetryPolicy _retry;
    private readonly MirrorOptions _options;
    private readonly MirrorStatus _status;
    private readonly ILogger<TunnelReconciler> _logger;
    private readonly Dictionary<TunnelSpec, ITunnel> _tunnels = new();

    // The container names of the last published ports, by host port, to name the ports that go away.
    private Dictionary<int, string> _containers = new();

    public TunnelReconciler(
        ITunnelFactory factory,
        TimeProvider timeProvider,
        IOptions<MirrorOptions> options,
        MirrorStatus status,
        ILogger<TunnelReconciler> logger)
    {
        _factory = factory;
        _retry = new RetryPolicy(timeProvider);
        _options = options.Value;
        _status = status;
        _logger = logger;
    }

    /// <summary>The tunnels that are currently open.</summary>
    public IReadOnlyCollection<TunnelSpec> OpenTunnels => _tunnels.Keys;

    public async Task ReconcileAsync(IReadOnlySet<PublishedPort> publishedPorts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(publishedPorts);
        var desired = TunnelSpec.FromPublishedPorts(publishedPorts, _options.BindAddress);
        var containers = ContainersByPort(publishedPorts);

        await RemoveExitedTunnelsAsync(desired, containers).ConfigureAwait(false);

        // A tunnel still alive one poll after it opened is healthy: forget its past failures.
        foreach (var spec in _tunnels.Keys)
            _retry.Reset(spec);

        var diff = PortSetDiff.Compute(_tunnels.Keys, desired);
        foreach (var spec in diff.ToRemove.OrderBy(spec => spec.LocalPort))
        {
            await CloseAsync(spec).ConfigureAwait(false);
            Log.TunnelClosed(_logger, spec.LocalPort, ContainerOf(_containers, spec));
        }

        _containers = containers;
        _retry.Retain(desired);

        foreach (var spec in diff.ToAdd.Where(_retry.IsDue).OrderBy(spec => spec.LocalPort))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await OpenAsync(spec, cancellationToken).ConfigureAwait(false);
        }

        PublishStatus(desired);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var spec in _tunnels.Keys.ToArray())
            await CloseAsync(spec).ConfigureAwait(false);
        _status.SetPorts([]);
    }

    private async Task OpenAsync(TunnelSpec spec, CancellationToken cancellationToken)
    {
        try
        {
            var tunnel = await _factory.OpenAsync(spec, cancellationToken).ConfigureAwait(false);
            _tunnels[spec] = tunnel;
            Log.TunnelOpened(_logger, spec.LocalPort, ContainerOf(_containers, spec), tunnel.Via, spec.RemoteHost, spec.RemotePort);
        }
        catch (DockerPortMirrorException ex)
        {
            ReportFailure(spec, ex.Message);
        }
    }

    private async Task RemoveExitedTunnelsAsync(IReadOnlySet<TunnelSpec> desired, Dictionary<int, string> containers)
    {
        foreach (var (spec, tunnel) in _tunnels.Where(pair => pair.Value.Completion.IsCompleted).ToArray())
        {
            var exit = await tunnel.Completion.ConfigureAwait(false);
            await CloseAsync(spec).ConfigureAwait(false);
            if (desired.Contains(spec))
                ReportFailure(spec, ContainerOf(containers, spec), $"tunnel exited: {exit.Reason}");
            else
                Log.TunnelClosed(_logger, spec.LocalPort, ContainerOf(_containers, spec));
        }
    }

    private void ReportFailure(TunnelSpec spec, string reason) => ReportFailure(spec, ContainerOf(_containers, spec), reason);

    private void ReportFailure(TunnelSpec spec, string container, string reason)
    {
        var decision = _retry.Fail(spec, reason);
        var retrySeconds = (int)Math.Ceiling(decision.Delay.TotalSeconds);
        if (decision.IsNewState)
            Log.TunnelFailed(_logger, spec.LocalPort, container, reason, retrySeconds);
        else
            Log.TunnelStillFailing(_logger, spec.LocalPort, container, decision.Failures, reason, retrySeconds);
    }

    /// <summary>Publishes every wanted port: open when its tunnel is live, otherwise retrying with its last failure.</summary>
    private void PublishStatus(IReadOnlySet<TunnelSpec> desired)
    {
        var ports = new List<PortStatus>(desired.Count);
        foreach (var spec in desired.OrderBy(spec => spec.LocalPort))
        {
            var container = ContainerOf(_containers, spec);
            var remote = $"{spec.RemoteHost}:{spec.RemotePort}";
            if (_tunnels.TryGetValue(spec, out var tunnel))
                ports.Add(new PortStatus(spec.LocalPort, container, remote, tunnel.Via, PortState.Open, null, null));
            else if (_retry.TryGetPending(spec, out var reason, out var retryAt))
                ports.Add(new PortStatus(spec.LocalPort, container, remote, "", PortState.Retrying, reason, retryAt));
            else
                ports.Add(new PortStatus(spec.LocalPort, container, remote, "", PortState.Retrying, null, null));
        }

        _status.SetPorts(ports);
    }

    /// <summary>The names of the containers that publish each host port (rarely more than one, joined with <c>, </c>).</summary>
    private static Dictionary<int, string> ContainersByPort(IEnumerable<PublishedPort> ports) =>
        ports
            .GroupBy(port => port.HostPort)
            .ToDictionary(
                group => group.Key,
                group => string.Join(", ", group
                    .Select(port => port.Container)
                    .Where(name => name.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)));

    private static string ContainerOf(Dictionary<int, string> containers, TunnelSpec spec) =>
        containers.GetValueOrDefault(spec.LocalPort, "");

    private async Task CloseAsync(TunnelSpec spec)
    {
        if (_tunnels.Remove(spec, out var tunnel))
            await tunnel.DisposeAsync().ConfigureAwait(false);
    }
}
