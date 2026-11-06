using System.Net;
using DockerPortMirror.Cli;
using DockerPortMirror.Docker;
using DockerPortMirror.Mirroring;
using DockerPortMirror.Tests.Fakes;
using Microsoft.Extensions.Options;

namespace DockerPortMirror.Tests.Docker;

public sealed class DockerCliPortSourceTests
{
    private const string MacContext = """[{ "Endpoints": { "docker": { "Host": "ssh://macdocker" } } }]""";

    [Fact]
    public async Task Ports_are_read_from_docker_ps_on_the_resolved_context()
    {
        var runner = new FakeProcessRunner()
            .On("docker context inspect mac", 0, MacContext)
            .On("ssh -G macdocker", 0)
            .On("docker --context mac ps --format {{.Ports}}", 0, "0.0.0.0:8080->80/tcp\n");
        var source = new DockerCliPortSource(new DockerContextResolver(runner, Options.Create(new MirrorOptions { Context = "mac" })), runner);

        var ports = await source.GetPublishedPortsAsync(TestContext.Current.CancellationToken);

        Assert.Equal([new PublishedPort(IPAddress.Any, 8080)], ports);
    }

    [Fact]
    public async Task Failing_docker_ps_is_a_user_facing_error()
    {
        var runner = new FakeProcessRunner()
            .On("docker context inspect mac", 0, MacContext)
            .On("ssh -G macdocker", 0)
            .On("docker --context mac ps --format {{.Ports}}", 1, "", "Cannot connect to the Docker daemon");
        var source = new DockerCliPortSource(new DockerContextResolver(runner, Options.Create(new MirrorOptions { Context = "mac" })), runner);

        var error = await Assert.ThrowsAsync<DockerPortMirrorException>(() => source.GetPublishedPortsAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Cannot connect to the Docker daemon", error.Message, StringComparison.Ordinal);
    }
}
