using System.Net;
using DockerPortMirror.Cli;
using DockerPortMirror.Diagnostics;
using DockerPortMirror.Mirroring;
using DockerPortMirror.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DockerPortMirror.Tests.Mirroring;

public sealed class TunnelReconcilerTests : IAsyncDisposable
{
    private readonly FakeTunnelFactory _factory = new();
    private readonly ManualTimeProvider _time = new();
    private readonly RecordingLogger<TunnelReconciler> _logger = new();
    private readonly MirrorStatus _status = new();
    private readonly TunnelReconciler _reconciler;

    public TunnelReconcilerTests()
    {
        _reconciler = new TunnelReconciler(_factory, _time, Options.Create(new MirrorOptions()), _status, _logger);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static HashSet<PublishedPort> Ports(params int[] ports) =>
        ports.Select(port => new PublishedPort(IPAddress.Any, port)).ToHashSet();

    public ValueTask DisposeAsync() => _reconciler.DisposeAsync();

    private int[] OpenPorts => _reconciler.OpenTunnels.Select(spec => spec.LocalPort).Order().ToArray();

    [Fact]
    public async Task New_ports_are_opened_in_order()
    {
        await _reconciler.ReconcileAsync(Ports(8080, 5432), Token);

        Assert.Equal([5432, 8080], _factory.Attempts.Select(spec => spec.LocalPort));
        Assert.Equal([5432, 8080], OpenPorts);
        Assert.Equal(2, _logger.Records.Count(record => record.EventId.Id == Log.TunnelOpenedId));
    }

    [Fact]
    public async Task Tunnels_are_forwarded_to_loopback_on_the_bind_address()
    {
        await _reconciler.ReconcileAsync(Ports(8080), Token);

        Assert.Equal(new TunnelSpec("127.0.0.1", 8080, IPAddress.Loopback, 8080), Assert.Single(_factory.Attempts));
    }

    [Fact]
    public async Task Unchanged_ports_are_left_alone()
    {
        await _reconciler.ReconcileAsync(Ports(8080), Token);
        await _reconciler.ReconcileAsync(Ports(8080), Token);

        Assert.Single(_factory.Attempts);
    }

    [Fact]
    public async Task Removed_ports_are_closed()
    {
        await _reconciler.ReconcileAsync(Ports(8080, 5432), Token);

        await _reconciler.ReconcileAsync(Ports(5432), Token);

        Assert.Equal([5432], OpenPorts);
        Assert.True(_factory.Opened.Single(tunnel => tunnel.Spec.LocalPort == 8080).IsDisposed);
        Assert.Contains(_logger.Records, record => record.EventId.Id == Log.TunnelClosedId && record.Message.Contains("8080", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Exited_tunnel_is_reported_and_restarted_after_backoff()
    {
        await _reconciler.ReconcileAsync(Ports(8080), Token);
        _factory.Opened[0].Exit("Connection reset");

        await _reconciler.ReconcileAsync(Ports(8080), Token);

        Assert.Empty(OpenPorts);
        Assert.Contains(_logger.Records, record => record.EventId.Id == Log.TunnelFailedId && record.Message.Contains("Connection reset", StringComparison.Ordinal));

        _time.Advance(TimeSpan.FromSeconds(1));
        await _reconciler.ReconcileAsync(Ports(8080), Token);

        Assert.Equal([8080], OpenPorts);
        Assert.Equal(2, _factory.Attempts.Count);
    }

    [Fact]
    public async Task One_failing_port_does_not_block_the_others()
    {
        _factory.Failures[5432] = "bind: Address already in use";

        await _reconciler.ReconcileAsync(Ports(3000, 5432, 8080), Token);

        Assert.Equal([3000, 8080], OpenPorts);
        Assert.Equal([3000, 5432, 8080], _factory.Attempts.Select(spec => spec.LocalPort));
    }

    [Fact]
    public async Task Failing_port_is_retried_with_backoff_and_warned_about_once()
    {
        _factory.Failures[5432] = "bind: Address already in use";

        await _reconciler.ReconcileAsync(Ports(5432), Token);   // attempt 1, retry in 1s
        await _reconciler.ReconcileAsync(Ports(5432), Token);   // not due
        _time.Advance(TimeSpan.FromSeconds(1));
        await _reconciler.ReconcileAsync(Ports(5432), Token);   // attempt 2, retry in 2s
        _time.Advance(TimeSpan.FromSeconds(1));
        await _reconciler.ReconcileAsync(Ports(5432), Token);   // not due
        _time.Advance(TimeSpan.FromSeconds(1));
        await _reconciler.ReconcileAsync(Ports(5432), Token);   // attempt 3

        Assert.Equal(3, _factory.Attempts.Count);
        var warnings = _logger.Records.Where(record => record.Level == LogLevel.Warning).ToArray();
        var warning = Assert.Single(warnings);
        Assert.Contains("retry in 1s", warning.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Port_that_recovers_opens_and_forgets_its_failures()
    {
        _factory.Failures[5432] = "bind: Address already in use";
        await _reconciler.ReconcileAsync(Ports(5432), Token);

        _factory.Failures.Clear();
        _time.Advance(TimeSpan.FromSeconds(1));
        await _reconciler.ReconcileAsync(Ports(5432), Token);

        Assert.Equal([5432], OpenPorts);
    }

    [Fact]
    public async Task Port_that_disappears_while_failing_is_forgotten()
    {
        _factory.Failures[5432] = "bind: Address already in use";
        await _reconciler.ReconcileAsync(Ports(5432), Token);
        await _reconciler.ReconcileAsync(Ports(), Token);
        _factory.Failures.Clear();

        await _reconciler.ReconcileAsync(Ports(5432), Token);

        Assert.Equal([5432], OpenPorts);
    }

    [Fact]
    public async Task Dispose_closes_every_tunnel()
    {
        await _reconciler.ReconcileAsync(Ports(1, 2, 3), Token);

        await _reconciler.DisposeAsync();

        Assert.All(_factory.Opened, tunnel => Assert.True(tunnel.IsDisposed));
        Assert.Empty(_reconciler.OpenTunnels);
    }

    [Fact]
    public async Task Status_lists_open_and_retrying_ports_in_order()
    {
        _factory.Failures[3000] = "bind: Address already in use";

        await _reconciler.ReconcileAsync(Ports(8080, 3000), Token);

        Assert.Equal(
            [
                new PortStatus(3000, "", "127.0.0.1:3000", "", PortState.Retrying, "bind: Address already in use", _time.GetUtcNow() + TimeSpan.FromSeconds(1)),
                new PortStatus(8080, "", "127.0.0.1:8080", "fakehost", PortState.Open, null, null),
            ],
            _status.Snapshot().Ports);
    }

    [Fact]
    public async Task Status_and_log_lines_carry_the_container_names()
    {
        _factory.Failures[3000] = "in use";
        HashSet<PublishedPort> ports =
        [
            new(IPAddress.Any, 5432, "postgres"),
            new(IPAddress.IPv6Any, 5432, "postgres"),
            new(IPAddress.Any, 3000, "api"),
            new(IPAddress.Any, 9000, "b"),
            new(IPAddress.Parse("127.0.0.1"), 9000, "a"),
        ];

        await _reconciler.ReconcileAsync(ports, Token);
        Assert.Equal(["api", "postgres", "a, b"], _status.Snapshot().Ports.Select(port => port.Container));
        await _reconciler.ReconcileAsync(new HashSet<PublishedPort>(), Token);

        Assert.Contains(_logger.Records, record => record.EventId.Id == Log.TunnelOpenedId && record.Message.EndsWith("127.0.0.1:5432 (postgres)", StringComparison.Ordinal));
        Assert.Contains(_logger.Records, record => record.EventId.Id == Log.TunnelFailedId && record.Message.StartsWith(" ! localhost:3000 (api) failed: in use", StringComparison.Ordinal));
        Assert.Contains(_logger.Records, record => record.EventId.Id == Log.TunnelClosedId && record.Message == " - localhost:5432 (postgres)");
    }

    [Fact]
    public async Task Unknown_container_adds_nothing_to_log_lines()
    {
        await _reconciler.ReconcileAsync(Ports(8080), Token);

        Assert.Contains(_logger.Records, record => record.Message == " + localhost:8080 -> fakehost -> 127.0.0.1:8080");
    }

    [Fact]
    public async Task Status_is_empty_after_dispose()
    {
        await _reconciler.ReconcileAsync(Ports(8080), Token);
        Assert.Single(_status.Snapshot().Ports);

        await _reconciler.DisposeAsync();

        Assert.Empty(_status.Snapshot().Ports);
    }
}
