namespace DockerPortMirror.Mirroring;

/// <summary>Opens port forwards. Another transport (an in-process SSH client) is another implementation.</summary>
internal interface ITunnelFactory
{
    /// <exception cref="TunnelStartException">The tunnel could not be opened (for example, the local port is in use).</exception>
    /// <exception cref="DockerPortMirrorException">The transport is not available at all.</exception>
    Task<ITunnel> OpenAsync(TunnelSpec spec, CancellationToken cancellationToken);
}
