using System.Net;
using DockerPortMirror.Docker;
using DockerPortMirror.Mirroring;

namespace DockerPortMirror.Tests.Docker;

public sealed class DockerPortsParserTests
{
    private static PublishedPort Port(string ip, int port) => new(IPAddress.Parse(ip), port);

    [Fact]
    public void Empty_output_has_no_ports()
    {
        Assert.Empty(DockerPortsParser.Parse(""));
    }

    [Fact]
    public void Single_ipv4_port_is_parsed()
    {
        Assert.Equal([Port("0.0.0.0", 8080)], DockerPortsParser.Parse("0.0.0.0:8080->80/tcp"));
    }

    [Fact]
    public void Ipv4_and_ipv6_bindings_are_both_returned()
    {
        var ports = DockerPortsParser.Parse("0.0.0.0:5432->5432/tcp, [::]:5432->5432/tcp");

        Assert.Equal([Port("0.0.0.0", 5432), Port("::", 5432)], ports);
    }

    [Fact]
    public void Legacy_ipv6_any_format_is_parsed()
    {
        Assert.Equal([Port("::", 5432)], DockerPortsParser.Parse(":::5432->5432/tcp"));
    }

    [Theory]
    [InlineData("127.0.0.1:3000->3000/tcp", "127.0.0.1", 3000)]
    [InlineData("192.168.1.5:80->8080/tcp", "192.168.1.5", 80)]
    [InlineData("[::1]:9000->9000/tcp", "::1", 9000)]
    [InlineData("[fd00::5]:443->443/tcp", "fd00::5", 443)]
    public void Specific_host_ip_is_kept(string output, string ip, int port)
    {
        Assert.Equal([Port(ip, port)], DockerPortsParser.Parse(output));
    }

    [Fact]
    public void Port_range_is_expanded()
    {
        var ports = DockerPortsParser.Parse("0.0.0.0:8000-8002->8000-8002/tcp");

        Assert.Equal([Port("0.0.0.0", 8000), Port("0.0.0.0", 8001), Port("0.0.0.0", 8002)], ports);
    }

    [Fact]
    public void Udp_ports_are_ignored()
    {
        Assert.Empty(DockerPortsParser.Parse("0.0.0.0:53->53/udp, [::]:53->53/udp"));
    }

    [Fact]
    public void Exposed_but_unpublished_ports_are_ignored()
    {
        Assert.Empty(DockerPortsParser.Parse("80/tcp, 443/tcp"));
    }

    [Theory]
    [InlineData("0.0.0.0:0->80/tcp")]
    [InlineData("0.0.0.0:70000->80/tcp")]
    [InlineData("0.0.0.0:9001-9000->80/tcp")]
    [InlineData("not-an-ip:80->80/tcp")]
    public void Invalid_entries_are_ignored(string output)
    {
        Assert.Empty(DockerPortsParser.Parse(output));
    }

    [Fact]
    public void Several_containers_are_combined()
    {
        var ports = DockerPortsParser.Parse("0.0.0.0:8080->80/tcp\r\n127.0.0.1:3000->3000/tcp, 53/udp\n\n");

        Assert.Equal([Port("0.0.0.0", 8080), Port("127.0.0.1", 3000)], ports);
    }
}
