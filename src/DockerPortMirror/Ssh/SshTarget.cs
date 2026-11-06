namespace DockerPortMirror.Ssh;

internal static class SshTarget
{
    /// <summary>Derives an OpenSSH destination from an <c>ssh://</c> Docker endpoint.</summary>
    public static string? FromEndpoint(string endpoint)
    {
        if (!endpoint.StartsWith("ssh://", StringComparison.OrdinalIgnoreCase))
            return null;

        var target = endpoint["ssh://".Length..];
        var slashIndex = target.IndexOf('/', StringComparison.Ordinal);
        if (slashIndex >= 0)
            target = target[..slashIndex];

        return Uri.UnescapeDataString(target);
    }
}
