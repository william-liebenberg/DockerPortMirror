namespace DockerPortMirror.Processes;

/// <summary>Runs a short-lived command and collects its output.</summary>
internal interface IProcessRunner
{
    /// <summary>
    /// Runs <paramref name="fileName"/> with <paramref name="arguments"/> and waits for it to exit.
    /// The process tree is killed when <paramref name="timeout"/> passes or <paramref name="cancellationToken"/> is cancelled.
    /// </summary>
    /// <exception cref="DockerPortMirrorException">The program cannot be started, or it did not finish within the timeout.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken);
}
