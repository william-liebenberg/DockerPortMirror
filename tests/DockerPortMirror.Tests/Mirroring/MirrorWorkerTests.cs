using System.Net;
using DockerPortMirror.Cli;
using DockerPortMirror.Docker;
using DockerPortMirror.Mirroring;
using DockerPortMirror.Tests.Fakes;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DockerPortMirror.Tests.Mirroring;

public sealed class MirrorWorkerTests
{
    [Fact]
    public async Task Poll_failures_are_warned_about_once_and_do_not_stop_mirroring()
    {
        var source = new FakePortSource();
        var factory = new FakeTunnelFactory();
        var options = Options.Create(new MirrorOptions());
        var status = new MirrorStatus();
        await using var reconciler = new TunnelReconciler(factory, new ManualTimeProvider(), options, status, NullLogger<TunnelReconciler>.Instance);
        var logger = new RecordingLogger<MirrorWorker>();
        using var worker = new MirrorWorker(
            new DockerContextResolver(new FakeProcessRunner(), options),
            source,
            reconciler,
            status,
            options,
            TimeProvider.System,
            new FakeLifetime(),
            logger);
        var token = TestContext.Current.CancellationToken;

        Assert.False(status.Snapshot().HasPolled);
        source.Error = "Cannot connect to the Docker daemon";
        await worker.PollOnceAsync(token);
        await worker.PollOnceAsync(token);
        Assert.Equal("Cannot connect to the Docker daemon", status.Snapshot().DockerError);
        source.Error = null;
        source.Ports = new HashSet<PublishedPort> { new(IPAddress.Any, 8080) };
        await worker.PollOnceAsync(token);

        Assert.Single(logger.Records, record => record.Level == LogLevel.Warning);
        Assert.Equal([8080], reconciler.OpenTunnels.Select(spec => spec.LocalPort));
        Assert.True(status.Snapshot().HasPolled);
        Assert.Null(status.Snapshot().DockerError);
        Assert.Equal([8080], status.Snapshot().Ports.Select(port => port.LocalPort));
    }

    [Fact]
    public async Task Poll_failure_keeps_the_last_known_ports()
    {
        var source = new FakePortSource { Ports = new HashSet<PublishedPort> { new(IPAddress.Any, 5432, "postgres") } };
        var options = Options.Create(new MirrorOptions());
        var status = new MirrorStatus();
        await using var reconciler = new TunnelReconciler(new FakeTunnelFactory(), new ManualTimeProvider(), options, status, NullLogger<TunnelReconciler>.Instance);
        using var worker = new MirrorWorker(
            new DockerContextResolver(new FakeProcessRunner(), options),
            source,
            reconciler,
            status,
            options,
            TimeProvider.System,
            new FakeLifetime(),
            NullLogger<MirrorWorker>.Instance);
        var token = TestContext.Current.CancellationToken;

        await worker.PollOnceAsync(token);
        source.Error = "ssh: connect to host dockerhost: Network is unreachable";
        await worker.PollOnceAsync(token);

        var snapshot = status.Snapshot();
        Assert.Equal("ssh: connect to host dockerhost: Network is unreachable", snapshot.DockerError);
        Assert.Equal("postgres", Assert.Single(snapshot.Ports).Container);
    }

    private sealed class FakePortSource : IPublishedPortSource
    {
        public string? Error { get; set; }

        public IReadOnlySet<PublishedPort> Ports { get; set; } = new HashSet<PublishedPort>();

        public Task<IReadOnlySet<PublishedPort>> GetPublishedPortsAsync(CancellationToken cancellationToken) =>
            Error is null ? Task.FromResult(Ports) : throw new DockerPortMirrorException(Error);
    }

    private sealed class FakeLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
