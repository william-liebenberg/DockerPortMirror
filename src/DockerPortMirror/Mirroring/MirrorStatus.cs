using DockerPortMirror.Docker;

namespace DockerPortMirror.Mirroring;

internal enum PortState
{
    /// <summary>The tunnel is live.</summary>
    Open,

    /// <summary>The tunnel could not be opened or stopped, and waits for its next attempt.</summary>
    Retrying,
}

/// <summary>The state of one mirrored port, for display.</summary>
/// <param name="LocalPort">The local port, which is also the published port on the remote host.</param>
/// <param name="Container">The containers that publish the port, joined with <c>, </c>; empty when unknown.</param>
/// <param name="Remote">The address and port the tunnel reaches on the remote host.</param>
/// <param name="Via">The hop the tunnel goes through; empty while it is not open.</param>
/// <param name="State">Whether the tunnel is open or waits for a retry.</param>
/// <param name="Reason">Why the last attempt failed, while retrying.</param>
/// <param name="RetryAt">When the next attempt is due, while retrying.</param>
internal sealed record PortStatus(
    int LocalPort,
    string Container,
    string Remote,
    string Via,
    PortState State,
    string? Reason,
    DateTimeOffset? RetryAt);

/// <summary>What the dashboard shows, at one moment.</summary>
/// <param name="Endpoint">The resolved Docker context; <c>null</c> until it is resolved.</param>
/// <param name="HasPolled">Whether Docker answered or failed at least once.</param>
/// <param name="DockerError">Why the last <c>docker ps</c> failed; <c>null</c> when it succeeded.</param>
/// <param name="Ports">The mirrored ports, open or retrying.</param>
internal sealed record MirrorSnapshot(
    DockerEndpoint? Endpoint,
    bool HasPolled,
    string? DockerError,
    IReadOnlyList<PortStatus> Ports)
{
    public static readonly MirrorSnapshot Empty = new(null, false, null, []);
}

/// <summary>
/// The live state of the mirror. The worker and the reconciler write it; a user interface reads immutable
/// snapshots from any thread. It knows nothing about how it is displayed.
/// </summary>
internal sealed class MirrorStatus
{
    private readonly Lock _gate = new();
    private MirrorSnapshot _snapshot = MirrorSnapshot.Empty;

    public MirrorSnapshot Snapshot()
    {
        lock (_gate)
            return _snapshot;
    }

    public void SetEndpoint(DockerEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        lock (_gate)
            _snapshot = _snapshot with { Endpoint = endpoint };
    }

    /// <summary>Records the outcome of a poll: the error message, or <c>null</c> when Docker answered.</summary>
    public void SetDockerError(string? error)
    {
        lock (_gate)
            _snapshot = _snapshot with { HasPolled = true, DockerError = error };
    }

    public void SetPorts(IReadOnlyList<PortStatus> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        var copy = ports.ToArray();
        lock (_gate)
            _snapshot = _snapshot with { Ports = copy };
    }
}
