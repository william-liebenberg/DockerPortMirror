using System.Net;

namespace DockerPortMirror.Mirroring;

/// <summary>A TCP port that a container publishes on the remote Docker host.</summary>
internal readonly record struct PublishedPort
{
    public PublishedPort(IPAddress hostIp, int hostPort)
    {
        ArgumentNullException.ThrowIfNull(hostIp);
        ArgumentOutOfRangeException.ThrowIfLessThan(hostPort, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hostPort, 65535);
        HostIp = hostIp;
        HostPort = hostPort;
    }

    /// <summary>The address Docker binds on the remote host; <c>0.0.0.0</c> and <c>::</c> mean every interface.</summary>
    public IPAddress HostIp { get; }

    /// <summary>The port Docker binds on the remote host.</summary>
    public int HostPort { get; }

    /// <summary>Whether Docker binds every interface of the remote host.</summary>
    public bool IsAnyAddress => HostIp.Equals(IPAddress.Any) || HostIp.Equals(IPAddress.IPv6Any);

    /// <summary>The address to connect to from the remote host: loopback when Docker binds every interface.</summary>
    public IPAddress RemoteAddress => IsAnyAddress ? IPAddress.Loopback : HostIp;

    public override string ToString() =>
        HostIp.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{HostIp}]:{HostPort}" : $"{HostIp}:{HostPort}";
}
