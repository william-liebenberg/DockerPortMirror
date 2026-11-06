namespace DockerPortMirror.Ssh;

/// <summary>An OpenSSH destination: a host alias, <c>user@host</c>, or an <c>ssh://user@host:port</c> URI.</summary>
internal readonly record struct SshTarget
{
    public SshTarget(string destination)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        Destination = destination.Trim();
    }

    /// <summary>The destination argument passed to <c>ssh</c>.</summary>
    public string Destination { get; }

    /// <summary>
    /// Derives an OpenSSH destination from an <c>ssh://</c> Docker endpoint, or returns <c>null</c> for any
    /// other endpoint. Without a port the destination is <c>user@host</c>, which also matches host aliases
    /// in <c>~/.ssh/config</c>; with a port it stays an <c>ssh://</c> URI, which OpenSSH 7.7+ accepts.
    /// </summary>
    public static SshTarget? FromEndpoint(string endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        const string scheme = "ssh://";
        if (!endpoint.StartsWith(scheme, StringComparison.OrdinalIgnoreCase))
            return null;

        var authority = endpoint[scheme.Length..];
        var slashIndex = authority.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex >= 0)
            authority = authority[..slashIndex];

        if (authority.Length == 0)
            return null;

        return HasPort(authority)
            ? new SshTarget(scheme + authority)
            : new SshTarget(Uri.UnescapeDataString(authority));
    }

    private static bool HasPort(string authority)
    {
        var hostPort = authority[(authority.LastIndexOf('@') + 1)..];
        var colon = hostPort.LastIndexOf(':');
        if (colon < 0 || colon < hostPort.LastIndexOf(']'))
            return false;

        var port = hostPort[(colon + 1)..];
        return port.Length > 0 && port.All(char.IsAsciiDigit);
    }

    public override string ToString() => Destination;
}
