using DockerPortMirror.Cli;
using DockerPortMirror.Docker;
using DockerPortMirror.Mirroring;
using DockerPortMirror.Processes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DockerPortMirror.Ssh;

/// <summary>Opens tunnels as OpenSSH local forwards to the host of the resolved Docker context.</summary>
internal sealed class OpenSshTunnelFactory : ITunnelFactory
{
    internal const string SshExecutable = "ssh";
    internal static readonly TimeSpan StartupGrace = TimeSpan.FromMilliseconds(300);

    private readonly DockerContextResolver _resolver;
    private readonly IChildProcessTracker _tracker;
    private readonly MirrorOptions _options;
    private readonly ILogger<OpenSshTunnel> _logger;

    public OpenSshTunnelFactory(
        DockerContextResolver resolver,
        IChildProcessTracker tracker,
        IOptions<MirrorOptions> options,
        ILogger<OpenSshTunnel> logger)
    {
        _resolver = resolver;
        _tracker = tracker;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ITunnel> OpenAsync(TunnelSpec spec, CancellationToken cancellationToken)
    {
        var endpoint = await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        var arguments = OpenSshArguments.ForTunnel(endpoint.SshTarget, spec, _options.Verbose);
        return await OpenSshTunnel.StartAsync(
                SshExecutable,
                arguments,
                spec,
                endpoint.SshTarget.Destination,
                StartupGrace,
                _tracker,
                _logger,
                cancellationToken)
            .ConfigureAwait(false);
    }
}
