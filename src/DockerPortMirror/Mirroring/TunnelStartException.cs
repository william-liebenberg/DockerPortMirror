namespace DockerPortMirror.Mirroring;

/// <summary>A tunnel could not be opened. The reconciler retries the port later.</summary>
public class TunnelStartException : DockerPortMirrorException
{
    public TunnelStartException()
    {
    }

    public TunnelStartException(string message)
        : base(message)
    {
    }

    public TunnelStartException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
