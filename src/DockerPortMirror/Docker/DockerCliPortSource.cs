using DockerPortMirror.Mirroring;
using DockerPortMirror.Processes;

namespace DockerPortMirror.Docker;

/// <summary>
/// Reads published ports with one <c>docker ps</c> call per poll. Over an SSH context every Docker CLI call opens
/// an SSH connection, so this deliberately avoids a second call such as <c>docker inspect</c>: the same call
/// returns the container names with their ports.
/// </summary>
internal sealed class DockerCliPortSource : IPublishedPortSource
{
    internal const string Format = "{{.Names}}\t{{.Ports}}";

    private readonly DockerContextResolver _resolver;
    private readonly IProcessRunner _runner;

    public DockerCliPortSource(DockerContextResolver resolver, IProcessRunner runner)
    {
        _resolver = resolver;
        _runner = runner;
    }

    public async Task<IReadOnlySet<PublishedPort>> GetPublishedPortsAsync(CancellationToken cancellationToken)
    {
        var endpoint = await _resolver.ResolveAsync(cancellationToken).ConfigureAwait(false);
        var result = await _runner.RunAsync(
                "docker",
                ["--context", endpoint.Context, "ps", "--format", Format],
                DockerContextResolver.CommandTimeout,
                cancellationToken)
            .ConfigureAwait(false);
        if (!result.Succeeded)
            throw new DockerPortMirrorException($"Unable to query Docker context '{endpoint.Context}'. {result.ErrorDetail}".TrimEnd());

        return DockerPortsParser.Parse(result.StandardOutput);
    }
}
