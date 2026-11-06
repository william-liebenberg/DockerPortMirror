using DockerPortMirror.Docker;

namespace DockerPortMirror.Tests.Docker;

public sealed class DockerPortsParserTests
{
    [Fact]
    public void Empty_output_has_no_ports()
    {
        Assert.Empty(DockerPortsParser.Parse(""));
    }

    [Fact]
    public void Single_ipv4_port_is_parsed()
    {
        Assert.Equal([8080], DockerPortsParser.Parse("0.0.0.0:8080->80/tcp"));
    }

    [Fact]
    public void Ipv4_and_ipv6_bindings_of_the_same_port_are_one_port()
    {
        Assert.Equal([5432], DockerPortsParser.Parse("0.0.0.0:5432->5432/tcp, [::]:5432->5432/tcp"));
    }

    [Fact]
    public void Udp_ports_are_ignored()
    {
        Assert.Empty(DockerPortsParser.Parse("0.0.0.0:53->53/udp"));
    }

    [Fact]
    public void Exposed_but_unpublished_ports_are_ignored()
    {
        Assert.Empty(DockerPortsParser.Parse("80/tcp"));
    }

    [Fact]
    public void Several_containers_are_combined()
    {
        var ports = DockerPortsParser.Parse("0.0.0.0:8080->80/tcp\n127.0.0.1:3000->3000/tcp\n\n");
        Assert.Equal([3000, 8080], ports.Order());
    }

    [Fact]
    public void Characterisation_port_ranges_are_not_parsed_today()
    {
        Assert.Empty(DockerPortsParser.Parse("0.0.0.0:8000-8001->8000-8001/tcp"));
    }
}
