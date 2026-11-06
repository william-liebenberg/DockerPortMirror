namespace DockerPortMirror.Mirroring;

/// <summary>
/// Supplies the TCP ports currently published on the remote Docker host. Port filters or other discovery
/// mechanisms (<c>docker events</c>, Podman) are new implementations or decorators of this interface.
/// </summary>
internal interface IPublishedPortSource
{
    /// <exception cref="DockerPortMirrorException">The ports could not be read; the next poll tries again.</exception>
    Task<IReadOnlySet<PublishedPort>> GetPublishedPortsAsync(CancellationToken cancellationToken);
}
