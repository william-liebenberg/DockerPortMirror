namespace DockerPortMirror.Mirroring;

/// <summary>Why a tunnel stopped: the exit code of its process, if any, and its last error lines.</summary>
internal sealed record TunnelExit(int? ExitCode, IReadOnlyList<string> ErrorLines)
{
    /// <summary>A one-line explanation for the user, from the last error lines that are not diagnostics.</summary>
    public string Reason
    {
        get
        {
            var lines = ErrorLines
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith("debug", StringComparison.OrdinalIgnoreCase))
                .TakeLast(3)
                .Select(line => line.Trim())
                .ToArray();
            if (lines.Length > 0)
                return string.Join(" ", lines);
            return ExitCode is { } code ? $"exit code {code}" : "stopped";
        }
    }
}
