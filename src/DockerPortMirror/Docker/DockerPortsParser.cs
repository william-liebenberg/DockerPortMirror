using System.Text.RegularExpressions;

namespace DockerPortMirror.Docker;

internal static partial class DockerPortsParser
{
    [GeneratedRegex(@"(?:(?:\d{1,3}\.){3}\d{1,3}|\[::\]):(?<port>\d+)->\d+(?:-\d+)?/tcp", RegexOptions.IgnoreCase)]
    private static partial Regex PublishedTcpPort();

    /// <summary>Parses the output of <c>docker ps --format {{.Ports}}</c> into published TCP host ports.</summary>
    public static HashSet<int> Parse(string output)
    {
        var ports = new HashSet<int>();
        foreach (Match match in PublishedTcpPort().Matches(output))
        {
            if (int.TryParse(match.Groups["port"].Value, out var port) && port is >= 1 and <= 65535)
                ports.Add(port);
        }

        return ports;
    }
}
