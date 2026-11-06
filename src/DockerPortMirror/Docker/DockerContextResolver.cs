using System.Text.Json;

namespace DockerPortMirror.Docker;

internal static class DockerContextResolver
{
    /// <summary>Reads the Docker endpoint host from <c>docker context inspect</c> JSON output.</summary>
    public static string? ParseEndpoint(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array || document.RootElement.GetArrayLength() == 0)
            return null;

        var item = document.RootElement[0];
        return item.TryGetProperty("Endpoints", out var endpoints)
            && endpoints.TryGetProperty("docker", out var docker)
            && docker.TryGetProperty("Host", out var host)
                ? host.GetString()
                : null;
    }
}
