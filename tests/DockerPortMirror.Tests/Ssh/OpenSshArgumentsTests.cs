using System.Net;
using DockerPortMirror.Mirroring;
using DockerPortMirror.Ssh;

namespace DockerPortMirror.Tests.Ssh;

public sealed class OpenSshArgumentsTests
{
    [Fact]
    public void Forward_arguments_are_built()
    {
        var spec = new TunnelSpec("127.0.0.1", 5432, IPAddress.Loopback, 5432);

        var arguments = OpenSshArguments.ForTunnel(new SshTarget("macdocker"), spec, verbose: false);

        Assert.Equal(
            [
                "-N", "-T",
                "-o", "ExitOnForwardFailure=yes",
                "-o", "BatchMode=yes",
                "-o", "ServerAliveInterval=30",
                "-o", "ServerAliveCountMax=3",
                "-o", "ConnectTimeout=10",
                "-L", "127.0.0.1:5432:127.0.0.1:5432",
                "--", "macdocker",
            ],
            arguments);
    }

    [Fact]
    public void Verbose_adds_ssh_diagnostics_before_the_destination()
    {
        var spec = new TunnelSpec("localhost", 80, IPAddress.Loopback, 80);

        var arguments = OpenSshArguments.ForTunnel(new SshTarget("ssh://me@host:2222"), spec, verbose: true);

        Assert.Equal(["-v", "--", "ssh://me@host:2222"], arguments.TakeLast(3));
    }

    [Fact]
    public void Specific_and_ipv6_remote_addresses_are_forwarded_to()
    {
        var ipv4 = OpenSshArguments.ForTunnel(new SshTarget("h"), new TunnelSpec("127.0.0.1", 80, IPAddress.Parse("192.168.1.5"), 80), false);
        var ipv6 = OpenSshArguments.ForTunnel(new SshTarget("h"), new TunnelSpec("127.0.0.1", 9000, IPAddress.IPv6Loopback, 9000), false);

        Assert.Contains("127.0.0.1:80:192.168.1.5:80", ipv4);
        Assert.Contains("127.0.0.1:9000:[::1]:9000", ipv6);
    }
}
