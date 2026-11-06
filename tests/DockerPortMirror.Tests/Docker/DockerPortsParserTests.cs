using System.Net;
using DockerPortMirror.Docker;
using DockerPortMirror.Mirroring;

namespace DockerPortMirror.Tests.Docker;

public sealed class DockerPortsParserTests
{
    private static PublishedPort Port(string ip, int port, string container = "") => new(IPAddress.Parse(ip), port, container);

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

    [Fact]
    public void Container_name_comes_before_the_tab()
    {
        Assert.Equal([Port("0.0.0.0", 5432, "postgres")], DockerPortsParser.Parse("postgres\t0.0.0.0:5432->5432/tcp\n"));
    }

    [Fact]
    public void Every_port_of_a_container_gets_its_name()
    {
        var ports = DockerPortsParser.Parse(
            "postgres\t0.0.0.0:5432->5432/tcp, [::]:5432->5432/tcp\r\n" +
            "web\t0.0.0.0:8080->80/tcp, 0.0.0.0:8443->443/tcp\n" +
            "worker\t\n");

        Assert.Equal(
            [Port("0.0.0.0", 5432, "postgres"), Port("::", 5432, "postgres"), Port("0.0.0.0", 8080, "web"), Port("0.0.0.0", 8443, "web")],
            ports);
    }

    [Fact]
    public void Line_without_a_tab_has_no_container_name()
    {
        var port = Assert.Single(DockerPortsParser.Parse("0.0.0.0:8080->80/tcp\n"));

        Assert.Equal("", port.Container);
    }

    [Fact]
    public void Port_range_of_a_container_gets_its_name()
    {
        Assert.All(DockerPortsParser.Parse("api\t0.0.0.0:8000-8001->8000-8001/tcp"), port => Assert.Equal("api", port.Container));
    }
}
