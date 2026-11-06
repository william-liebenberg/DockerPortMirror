namespace DockerPortMirror.Mirroring;

/// <summary>A running port forward. Disposing it stops the forward.</summary>
internal interface ITunnel : IAsyncDisposable
{
    TunnelSpec Spec { get; }

    /// <summary>The hop the tunnel goes through, for display (for OpenSSH, the SSH destination).</summary>
    string Via { get; }

    /// <summary>Completes when the tunnel stops by itself, with the reason it stopped.</summary>
    Task<TunnelExit> Completion { get; }
}
