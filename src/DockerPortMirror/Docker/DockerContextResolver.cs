using System.Text.Json;
using DockerPortMirror.Cli;
using DockerPortMirror.Processes;
using DockerPortMirror.Ssh;
using Microsoft.Extensions.Options;

namespace DockerPortMirror.Docker;

/// <summary>
/// Finds the Docker context to mirror and the SSH destination of its host. The first result is kept for the
/// whole session, so the Docker port source and the SSH transport use the same endpoint.
/// </summary>
internal sealed class DockerContextResolver
{
    internal static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(30);

    private readonly IProcessRunner _runner;
    private readonly MirrorOptions _options;
    private readonly Lock _gate = new();
    private Task<DockerEndpoint>? _endpoint;

    public DockerContextResolver(IProcessRunner runner, IOptions<MirrorOptions> options)
    {
        _runner = runner;
        _options = options.Value;
    }

    /// <exception cref="DockerPortMirrorException">Docker or SSH is missing, or the context is not an SSH context.</exception>
    public Task<DockerEndpoint> ResolveAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
            return _endpoint ??= ResolveCoreAsync(cancellationToken);
    }

    private async Task<DockerEndpoint> ResolveCoreAsync(CancellationToken cancellationToken)
    {
        var context = _options.Context;
        if (string.IsNullOrWhiteSpace(context))
        {
            var current = await RunAsync("docker", ["context", "show"], "Unable to determine the current Docker context", cancellationToken)
                .ConfigureAwait(false);
            context = current.StandardOutput.Trim();
            if (context.Length == 0)
                throw new DockerPortMirrorException("Docker did not return a current context.");
        }

        var inspect = await RunAsync("docker", ["context", "inspect", context], $"Unable to inspect Docker context '{context}'", cancellationToken)
            .ConfigureAwait(false);
        var host = ParseEndpoint(inspect.StandardOutput)
            ?? throw new DockerPortMirrorException($"Docker context '{context}' has no Docker endpoint.");

        var sshTarget = string.IsNullOrWhiteSpace(_options.SshTarget)
            ? SshTarget.FromEndpoint(host)
            : new SshTarget(_options.SshTarget);
        if (sshTarget is not { } target)
        {
            throw new DockerPortMirrorException(
                $"The Docker endpoint of context '{context}' is not SSH-based: '{host}'. " +
                "Select an ssh:// Docker context or pass --ssh-target <alias-or-user@host>.");
        }

        // ssh -G only resolves the configuration; it does not connect.
        await RunAsync("ssh", ["-G", target.Destination], $"OpenSSH could not resolve SSH target '{target}'", cancellationToken)
            .ConfigureAwait(false);

        return new DockerEndpoint(context, host, target);
    }

    private async Task<ProcessResult> RunAsync(string fileName, string[] arguments, string failure, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(fileName, arguments, CommandTimeout, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
            throw new DockerPortMirrorException($"{failure}. {result.ErrorDetail}".TrimEnd());
        return result;
    }

    /// <summary>Reads the Docker endpoint host from <c>docker context inspect</c> JSON output.</summary>
    internal static string? ParseEndpoint(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
                return null;

            var item = document.RootElement[0];
            return item.ValueKind == JsonValueKind.Object
                && item.TryGetProperty("Endpoints", out var endpoints)
                && endpoints.ValueKind == JsonValueKind.Object
                && endpoints.TryGetProperty("docker", out var docker)
                && docker.ValueKind == JsonValueKind.Object
                && docker.TryGetProperty("Host", out var host)
                && host.ValueKind == JsonValueKind.String
                    ? host.GetString()
                    : null;
        }
        catch (JsonException ex)
        {
            throw new DockerPortMirrorException($"Unexpected output from 'docker context inspect': {ex.Message}", ex);
        }
    }
}
