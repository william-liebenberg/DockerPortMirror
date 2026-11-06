namespace DockerPortMirror.Cli;

/// <summary>The options given on the command line; <c>null</c> means "not given", so configuration applies.</summary>
internal sealed record MirrorCommandLine(
    string? Context = null,
    string? SshTarget = null,
    string? BindAddress = null,
    int? PollSeconds = null,
    bool Verbose = false,
    FileInfo? ConfigFile = null,
    bool NoDashboard = false)
{
    /// <summary>The given options as configuration keys of <see cref="MirrorOptions"/>.</summary>
    public IEnumerable<KeyValuePair<string, string?>> ToConfiguration()
    {
        if (Context is not null)
            yield return new(nameof(MirrorOptions.Context), Context);
        if (SshTarget is not null)
            yield return new(nameof(MirrorOptions.SshTarget), SshTarget);
        if (BindAddress is not null)
            yield return new(nameof(MirrorOptions.BindAddress), BindAddress);
        if (PollSeconds is { } poll)
            yield return new(nameof(MirrorOptions.PollSeconds), poll.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Verbose)
            yield return new(nameof(MirrorOptions.Verbose), bool.TrueString);
        if (NoDashboard)
            yield return new(nameof(MirrorOptions.NoDashboard), bool.TrueString);
    }
}
