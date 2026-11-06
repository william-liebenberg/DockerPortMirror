namespace DockerPortMirror;

/// <summary>
/// A failure caused by the environment or the configuration (Docker or SSH missing, a bad context, a port in use)
/// rather than by a bug. Its message is shown to the user as is, without a stack trace.
/// </summary>
public class DockerPortMirrorException : Exception
{
    public DockerPortMirrorException()
    {
    }

    public DockerPortMirrorException(string message)
        : base(message)
    {
    }

    public DockerPortMirrorException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
