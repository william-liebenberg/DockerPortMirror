using System.Net;
using DockerPortMirror.Mirroring;

namespace DockerPortMirror.Tests.Mirroring;

public sealed class TunnelSpecTests
{
    private static PublishedPort Port(string ip, int port) => new(IPAddress.Parse(ip), port);

    [Theory]
    [InlineData("0.0.0.0", "127.0.0.1")]
    [InlineData("::", "127.0.0.1")]
    [InlineData("127.0.0.1", "127.0.0.1")]
    [InlineData("192.168.1.5", "192.168.1.5")]
    [InlineData("::1", "::1")]
    public void Remote_address_is_loopback_only_for_any(string hostIp, string expected)
    {
        Assert.Equal(IPAddress.Parse(expected), Port(hostIp, 80).RemoteAddress);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Published_port_must_be_in_range(int port)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PublishedPort(IPAddress.Any, port));
    }

    [Fact]
    public void Same_port_on_ipv4_and_ipv6_gives_one_tunnel()
    {
        var specs = TunnelSpec.FromPublishedPorts([Port("0.0.0.0", 5432), Port("::", 5432)], "127.0.0.1");

        Assert.Equal([new TunnelSpec("127.0.0.1", 5432, IPAddress.Loopback, 5432)], specs);
    }

    [Fact]
    public void Any_binding_is_preferred_over_a_specific_address()
    {
        var specs = TunnelSpec.FromPublishedPorts([Port("192.168.1.5", 80), Port("::", 80)], "localhost");

        Assert.Equal([new TunnelSpec("localhost", 80, IPAddress.Loopback, 80)], specs);
    }

    [Fact]
    public void Specific_address_is_forwarded_to()
    {
        var specs = TunnelSpec.FromPublishedPorts([Port("192.168.1.5", 80)], "127.0.0.1");

        var spec = Assert.Single(specs);
        Assert.Equal(IPAddress.Parse("192.168.1.5"), spec.RemoteAddress);
        Assert.Equal("192.168.1.5", spec.RemoteHost);
    }

    [Fact]
    public void Ipv6_remote_host_is_bracketed()
    {
        var spec = Assert.Single(TunnelSpec.FromPublishedPorts([Port("::1", 9000)], "127.0.0.1"));

        Assert.Equal("[::1]", spec.RemoteHost);
    }
}
