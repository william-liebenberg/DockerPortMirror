using System.Net;

namespace DockerPortMirror.Mirroring;

/// <summary>A TCP port that a container publishes on the remote Docker host.</summary>
internal readonly record struct PublishedPort
{
    public PublishedPort(IPAddress hostIp, int hostPort, string container = "")
    {
        ArgumentNullException.ThrowIfNull(hostIp);
        ArgumentOutOfRangeException.ThrowIfLessThan(hostPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hostPort, 65535);
        ArgumentNullException.ThrowIfNull(container);
        HostIp = hostIp;
        HostPort = hostPort;
        Container = container;
    }

    /// <summary>The address Docker binds on the remote host; <c>0.0.0.0</c> and <c>::</c> mean every interface.</summary>
    public IPAddress HostIp { get; }

    /// <summary>The port Docker binds on the remote host.</summary>
    public int HostPort { get; }

    /// <summary>The name of the container that publishes the port, for display; empty when unknown.</summary>
    public string Container { get; }

    /// <summary>Whether Docker binds every interface of the remote host.</summary>
    public bool IsAnyAddress => HostIp.Equals(IPAddress.Any) || HostIp.Equals(IPAddress.IPv6Any);

    /// <summary>The address to connect to from the remote host: loopback when Docker binds every interface.</summary>
    public IPAddress RemoteAddress => IsAnyAddress ? IPAddress.Loopback : HostIp;

    public override string ToString() =>
        HostIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{HostIp}]:{HostPort}" : $"{HostIp}:{HostPort}";
}
