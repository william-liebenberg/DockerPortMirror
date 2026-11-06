namespace DockerPortMirror.Cli;

/// <summary>
/// Settings of a mirroring session. Bound from the configuration file, <c>DOCKERPORTMIRROR_*</c> environment
/// variables and command-line options, in increasing order of precedence.
/// </summary>
public sealed class MirrorOptions
{
    public const int DefaultPollSeconds = 2;
    public const string DefaultBindAddress = "127.0.0.1";

    /// <summary>Allowed local bind addresses. Binding other interfaces would expose the remote ports to the network.</summary>
    public static readonly IReadOnlyList<string> AllowedBindAddresses = ["127.0.0.1", "localhost"];

    /// <summary>Docker context name; <c>null</c> uses <c>docker context show</c>.</summary>
    public string? Context { get; set; }

    /// <summary>OpenSSH destination; <c>null</c> derives it from the context's <c>ssh://</c> endpoint.</summary>
    public string? SshTarget { get; set; }

    public string BindAddress { get; set; } = DefaultBindAddress;

    public int PollSeconds { get; set; } = DefaultPollSeconds;

    public bool Verbose { get; set; }

    /// <summary>Print one line per event instead of the live dashboard (always the case when output is redirected).</summary>
    public bool NoDashboard { get; set; }
}
