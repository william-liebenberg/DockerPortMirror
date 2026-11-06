namespace DockerPortMirror.Processes;

/// <summary>The outcome of a process that ran to completion.</summary>
internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;

    /// <summary>The most useful text to show when the process failed: standard error, else standard output.</summary>
    public string ErrorDetail => (string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError).Trim();
}
