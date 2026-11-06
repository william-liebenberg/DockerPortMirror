using System.Globalization;
using System.Text;
using DockerPortMirror.Cli;
using DockerPortMirror.Diagnostics;
using DockerPortMirror.Mirroring;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DockerPortMirror.Dashboard;

/// <summary>
/// Draws one frame of the dashboard: a header with the connection, the mirrored ports grouped by container, and
/// the newest log lines that fit. Every line is cut to the width of the terminal, so the header and the table never
/// wrap, and the log panel takes the height that is left.
/// </summary>
internal static class DashboardView
{
    /// <summary>The log panel keeps at least this many lines, even in a small terminal.</summary>
    public const int MinimumLogLines = 3;

    private const int MinimumWidth = 20;
    private const int MaximumContainerWidth = 24;
    private const int ColumnGap = 2;
    private const string Gap = "    ";

    private static readonly Symbols UnicodeSymbols = new("●", "✗", "…");
    private static readonly Symbols AsciiSymbols = new("*", "x", "...");

    public static IRenderable Render(MirrorOptions options, MirrorSnapshot snapshot, IReadOnlyList<LogLine> log, DateTimeOffset now, Profile profile)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(profile);

        var symbols = profile.Capabilities.Unicode ? UnicodeSymbols : AsciiSymbols;
        var width = Math.Max(profile.Width, MinimumWidth);
        var inner = width - 4; // Panel borders and padding.

        var header = Header(options, snapshot, inner, symbols);
        var ports = Ports(snapshot, now, inner, symbols);

        // One line stays free, so the live region never scrolls the terminal.
        var renderOptions = new RenderOptions(profile.Capabilities, new Size(width, profile.Height));
        var used = HeightOf(header, renderOptions, width) + HeightOf(ports, renderOptions, width);
        var logLines = Math.Max(MinimumLogLines, profile.Height - 1 - used - 2);

        return new Rows(header, ports, Log(log, logLines, inner, symbols));
    }

    private static Panel Header(MirrorOptions options, MirrorSnapshot snapshot, int width, Symbols symbols)
    {
        Part[] first = snapshot.Endpoint is { } endpoint
            ?
            [
                Label("Context  "), new(endpoint.Context), new($" ({endpoint.Host})", "grey", Flexible: true),
                Label(Gap + "SSH  "), new(endpoint.SshTarget.Destination),
            ]
            : [Label("Context  "), new("resolving" + symbols.Ellipsis, "yellow")];

        Part docker = snapshot.DockerError is { } error
            ? new($"{symbols.Failed} {OneLine(error.Trim())}", "red", Flexible: true)
            : snapshot.HasPolled
                ? new($"{symbols.Ok} reachable", "green")
                : new("connecting" + symbols.Ellipsis, "yellow");
        Part[] second =
        [
            Label("Bind  "), new(options.BindAddress),
            Label(Gap + "Poll  "), new(options.PollSeconds.ToString(CultureInfo.InvariantCulture) + "s"),
            Label(Gap + "Docker  "), docker,

            // While Docker is down, its error needs the room more than the hint.
            Label(snapshot.DockerError is null ? Gap + "Ctrl+C to stop" : ""),
        ];

        return new Panel(new Rows(Line(first, width, symbols), Line(second, width, symbols)))
        {
            Header = new PanelHeader(Markup.Escape($" {AppInfo.Name} {AppInfo.Version} ")),
            Border = BoxBorder.Rounded,
            BorderStyle = snapshot.DockerError is null ? Style.Plain : new Style(Color.Red),
            Expand = true,
        };
    }

    private static Panel Ports(MirrorSnapshot snapshot, DateTimeOffset now, int width, Symbols symbols)
    {
        IRenderable body = snapshot.Ports.Count == 0
            ? Line([new(snapshot.HasPolled ? "No published TCP ports." : "Waiting for Docker" + symbols.Ellipsis, "grey")], width, symbols)
            : PortTable(snapshot.Ports, now, width, symbols);

        return new Panel(body)
        {
            Header = new PanelHeader(Markup.Escape($" Mirrored ports ({snapshot.Ports.Count}) ")),
            Border = BoxBorder.Rounded,
            Expand = true,
        };
    }

    private static Table PortTable(IReadOnlyList<PortStatus> ports, DateTimeOffset now, int width, Symbols symbols)
    {
        // Sorted by container then port, with unnamed ports last, so the ports of one container read together.
        var rows = ports
            .OrderBy(port => port.Container.Length == 0)
            .ThenBy(port => port.Container, StringComparer.OrdinalIgnoreCase)
            .ThenBy(port => port.LocalPort)
            .Select(port => new PortRow(
                port.Container.Length == 0 ? "-" : port.Container,
                $"localhost:{port.LocalPort}",
                port.Remote,
                StatusText(port, now, symbols),
                port.State == PortState.Open ? "green" : "red"))
            .ToArray();

        var containerWidth = Math.Min(MaximumContainerWidth, Math.Max("Container".Length, rows.Max(row => row.Container.Length)));
        var localWidth = Math.Max("Local".Length, rows.Max(row => row.Local.Length));
        var remoteWidth = Math.Max("Remote".Length, rows.Max(row => row.Remote.Length));
        var statusWidth = Math.Max(1, width - containerWidth - localWidth - remoteWidth - (3 * ColumnGap));

        var table = new Table { Border = TableBorder.None, ShowHeaders = true };
        table.AddColumn(Column("Container", ColumnGap));
        table.AddColumn(Column("Local", ColumnGap));
        table.AddColumn(Column("Remote", ColumnGap));
        table.AddColumn(Column("Status", 0));

        string? previous = null;
        foreach (var row in rows)
        {
            // The container name is shown once per group.
            var container = string.Equals(row.Container, previous, StringComparison.Ordinal) ? "" : row.Container;
            previous = row.Container;
            table.AddRow(
                Line([new(container, row.Container == "-" ? "grey" : null)], containerWidth, symbols),
                Line([new(row.Local)], localWidth, symbols),
                Line([new(row.Remote)], remoteWidth, symbols),
                Line([new(row.Status, row.StatusStyle)], statusWidth, symbols));
        }

        return table;
    }

    private static TableColumn Column(string header, int rightPadding) =>
        new(new Markup($"[grey]{header}[/]")) { Padding = new Padding(0, 0, rightPadding, 0), NoWrap = true };

    private static string StatusText(PortStatus port, DateTimeOffset now, Symbols symbols)
    {
        if (port.State == PortState.Open)
            return $"{symbols.Ok} open";

        var retry = port.RetryAt is { } retryAt
            ? Math.Max(0, (int)Math.Ceiling((retryAt - now).TotalSeconds)) is var seconds and > 0
                ? $"retry in {seconds.ToString(CultureInfo.InvariantCulture)}s"
                : "retrying"
            : "starting";
        return port.Reason is { Length: > 0 } reason
            ? $"{symbols.Failed} {retry}: {OneLine(reason.Trim())}"
            : $"{symbols.Failed} {retry}";
    }

    private static Panel Log(IReadOnlyList<LogLine> log, int height, int width, Symbols symbols)
    {
        var lines = log.Skip(Math.Max(0, log.Count - height)).Select(line => LogRow(line, width, symbols)).ToList();
        if (lines.Count == 0)
            lines.Add(Line([new("No messages yet.", "grey")], width, symbols));

        return new Panel(new Rows(lines))
        {
            Header = new PanelHeader(" Log "),
            Border = BoxBorder.Rounded,
            Expand = true,
        };
    }

    private static Markup LogRow(LogLine line, int width, Symbols symbols)
    {
        // Colours and prefixes follow the plain console output (MirrorConsoleFormatter).
        var (prefix, style) = line.EventId switch
        {
            Diagnostics.Log.TunnelOpenedId => ("", "green"),
            Diagnostics.Log.TunnelClosedId => ("", "yellow"),
            Diagnostics.Log.TunnelFailedId => ("", "red"),
            _ => line.Level switch
            {
                LogLevel.Trace or LogLevel.Debug => ("", "grey"),
                LogLevel.Warning => ("WARNING: ", "yellow"),
                LogLevel.Error or LogLevel.Critical => ("ERROR: ", "red"),
                _ => ("", (string?)null),
            },
        };

        var time = line.IsContinuation ? "" : line.Time.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var text = line.IsContinuation ? OneLine(line.Text) : prefix + OneLine(line.Text).TrimStart();
        return Line([new(time.PadRight(10), "grey"), new(text, style, Flexible: true)], width, symbols);
    }

    /// <summary>
    /// Builds one line of markup no wider than <paramref name="width"/>: flexible parts are shortened first, then
    /// whatever is at the end, with an ellipsis where text is cut.
    /// </summary>
    private static Markup Line(IReadOnlyList<Part> parts, int width, Symbols symbols)
    {
        var texts = parts.Select(part => part.Text).ToArray();
        var overflow = texts.Sum(text => text.Length) - width;
        for (var i = 0; i < texts.Length && overflow > 0; i++)
        {
            if (parts[i].Flexible)
                overflow -= Shorten(ref texts[i], Math.Max(1 + symbols.Ellipsis.Length, texts[i].Length - overflow), symbols);
        }

        for (var i = texts.Length - 1; i >= 0 && overflow > 0; i--)
            overflow -= Shorten(ref texts[i], Math.Max(0, texts[i].Length - overflow), symbols);

        var markup = new StringBuilder();
        for (var i = 0; i < texts.Length; i++)
        {
            if (texts[i].Length == 0)
                continue;
            var escaped = Markup.Escape(texts[i]);
            markup.Append(parts[i].Style is { } style ? $"[{style}]{escaped}[/]" : escaped);
        }

        return new Markup(markup.ToString());
    }

    /// <summary>Cuts <paramref name="text"/> to at most <paramref name="length"/> characters and returns how many it removed.</summary>
    private static int Shorten(ref string text, int length, Symbols symbols)
    {
        if (text.Length <= length)
            return 0;

        var before = text.Length;
        text = length > symbols.Ellipsis.Length
            ? text[..(length - symbols.Ellipsis.Length)] + symbols.Ellipsis
            : text[..length];
        return before - text.Length;
    }

    /// <summary>The text on one line: line breaks, tabs and other control characters become spaces.</summary>
    private static string OneLine(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.TrimEnd())
            builder.Append(char.IsControl(c) ? ' ' : c);
        return builder.ToString();
    }

    private static int HeightOf(IRenderable renderable, RenderOptions options, int width) =>
        Segment.SplitLines(renderable.Render(options, width)).Count;

    private static Part Label(string text) => new(text, "grey");

    private readonly record struct Part(string Text, string? Style = null, bool Flexible = false);

    private sealed record PortRow(string Container, string Local, string Remote, string Status, string StatusStyle);

    private sealed record Symbols(string Ok, string Failed, string Ellipsis);
}
