using System.Diagnostics;

namespace DockerPortMirror.Processes;

/// <summary>Ties child processes to the life of this process, so they do not outlive it even after a crash.</summary>
internal interface IChildProcessTracker
{
    /// <summary>Tracks a started process. Best effort: never throws.</summary>
    void Track(Process process);
}

/// <summary>Used where the OS offers no such mechanism; cleanup then relies on the host's graceful shutdown.</summary>
internal sealed class NoOpChildProcessTracker : IChildProcessTracker
{
    public void Track(Process process)
    {
    }
}
