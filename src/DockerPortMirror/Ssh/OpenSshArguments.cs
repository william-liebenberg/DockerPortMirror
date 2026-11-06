using DockerPortMirror.Mirroring;

namespace DockerPortMirror.Ssh;

/// <summary>Builds the <c>ssh</c> command line of one local port forward.</summary>
internal static class OpenSshArguments
{
    public static IReadOnlyList<string> ForTunnel(SshTarget target, TunnelSpec spec, bool verbose)
    {
        List<string> arguments =
        [
            "-N", // no remote command
            "-T", // no terminal
            "-o", "ExitOnForwardFailure=yes", // exit at once when the local port cannot be bound
            "-o", "BatchMode=yes", // never prompt for a password or passphrase
            "-o", "ServerAliveInterval=30",
            "-o", "ServerAliveCountMax=3",
            "-o", "ConnectTimeout=10",
            "-L", $"{spec.BindAddress}:{spec.LocalPort}:{spec.RemoteHost}:{spec.RemotePort}",
        ];

        if (verbose)
            arguments.Add("-v");

        // Last, so that a destination starting with '-' cannot be read as an option.
        arguments.Add("--");
        arguments.Add(target.Destination);
        return arguments;
    }
}
