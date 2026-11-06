using System.Net;
using System.Net.Sockets;

namespace DockerPortMirror.Mirroring;

/// <summary>What one tunnel forwards: a local bind address and port to an address and port on the remote host.</summary>
internal readonly record struct TunnelSpec(string BindAddress, int LocalPort, IPAddress RemoteAddress, int RemotePort)
{
    /// <summary>
    /// Turns the published ports into one tunnel per local port. When the same port is published on several
    /// addresses (typically <c>0.0.0.0</c> and <c>::</c>), the "any" binding wins, then loopback, then the
    /// lowest address, so the choice is stable from one poll to the next.
    /// </summary>
    public static IReadOnlySet<TunnelSpec> FromPublishedPorts(IEnumerable<PublishedPort> ports, string bindAddress)
    {
        ArgumentNullException.ThrowIfNull(ports);
        return ports
            .GroupBy(port => port.HostPort)
            .Select(group => group
                .OrderByDescending(port => port.IsAnyAddress)
                .ThenByDescending(port => IPAddress.IsLoopback(port.HostIp))
                .ThenBy(port => port.HostIp.AddressFamily == AddressFamily.InterNetworkV6)
                .ThenBy(port => port.HostIp.ToString(), StringComparer.Ordinal)
                .First())
            .Select(port => new TunnelSpec(bindAddress, port.HostPort, port.RemoteAddress, port.HostPort))
            .ToHashSet();
    }

    /// <summary>The remote host as OpenSSH expects it in a forwarding specification.</summary>
    public string RemoteHost =>
        RemoteAddress.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{RemoteAddress}]" : RemoteAddress.ToString();

    public override string ToString() => $"{BindAddress}:{LocalPort} -> {RemoteHost}:{RemotePort}";
}
