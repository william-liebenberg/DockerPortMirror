using System.Net;
using System.Text.RegularExpressions;
using DockerPortMirror.Mirroring;

namespace DockerPortMirror.Docker;

internal static partial class DockerPortsParser
{
    // One entry of {{.Ports}}: "0.0.0.0:8080->80/tcp", "[::]:8000-8001->8000-8001/tcp", ":::5432->5432/tcp".
    // Entries without "->" are exposed but not published, and are ignored.
    [GeneratedRegex(
        @"^(?<host>\[[0-9A-Fa-f:.%a-zA-Z]+\]|[0-9A-Fa-f:.]*):(?<start>\d{1,5})(?:-(?<end>\d{1,5}))?->\d{1,5}(?:-\d{1,5})?/(?<protocol>[a-z]+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex PublishedPortEntry();

    private static readonly char[] Separators = [',', ' ', '\t', '\r', '\n'];

    /// <summary>Parses the output of <c>docker ps --format {{.Ports}}</c> into published TCP ports.</summary>
    public static IReadOnlySet<PublishedPort> Parse(string output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var ports = new HashSet<PublishedPort>();
        foreach (var entry in output.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = PublishedPortEntry().Match(entry);
            if (!match.Success || !match.Groups["protocol"].Value.Equals("tcp", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!TryParseHost(match.Groups["host"].Value, out var hostIp))
                continue;

            var start = int.Parse(match.Groups["start"].Value, System.Globalization.CultureInfo.InvariantCulture);
            var end = match.Groups["end"].Success
                ? int.Parse(match.Groups["end"].Value, System.Globalization.CultureInfo.InvariantCulture)
                : start;
            if (start < 1 || end > 65535 || end < start)
                continue;

            for (var port = start; port <= end; port++)
                ports.Add(new PublishedPort(hostIp, port));
        }

        return ports;
    }

    private static bool TryParseHost(string host, out IPAddress address)
    {
        if (host.Length == 0)
        {
            address = IPAddress.Any;
            return true;
        }

        if (host.StartsWith('[') && host.EndsWith(']'))
            host = host[1..^1];

        return IPAddress.TryParse(host, out address!);
    }
}
