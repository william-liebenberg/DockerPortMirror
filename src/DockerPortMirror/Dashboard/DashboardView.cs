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
/// Draws the banner and connection details above the mirrored ports and recent logs. The content panels share
/// the available height on wide terminals and stack on narrow ones.
/// </summary>
internal static class DashboardView
{
    /// <summary>The rendered view keeps at least this many log lines when available, even if the live frame is cropped.</summary>
    public const int MinimumLogLines = 3;

    private const int MinimumWidth = 20;
    private const int SideBySideWidth = 80;
    private const int PanelGutter = 1;
    private const int PanelWidthOverhead = 4; // Borders and horizontal padding.
    private const int PanelHeightOverhead = 2; // Top and bottom borders.
    private const int MaximumContainerWidth = 24;
    private const int ColumnGap = 1;
    private const string Gap = "    ";

    private static readonly Symbols UnicodeSymbols = new("●", "✗", "❌", "…");
    private static readonly Symbols AsciiSymbols = new("*", "x", "x", "...");

    public static IRenderable Render(MirrorOptions options, MirrorSnapshot snapshot, IReadOnlyList<LogLine> log, DateTimeOffset now, Profile profile)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(profile);

        var symbols = profile.Capabilities.Unicode ? UnicodeSymbols : AsciiSymbols;
        var width = Math.Max(profile.Width, MinimumWidth);
        var inner = width - PanelWidthOverhead;
        var banner = new RainbowBanner();
        var header = Header(options, snapshot, inner, symbols);

        // One line stays free when possible, so the live region never scrolls the terminal.
        var renderOptions = new RenderOptions(profile.Capabilities, new Size(width, profile.Height));
        var available = profile.Height - 1 - HeightOf(banner, renderOptions, width) - HeightOf(header, renderOptions, width);
        if (width >= SideBySideWidth)
        {
            // Give the four-column table more room than the single-column log.
            var contentWidth = width - PanelGutter;
            var portsWidth = contentWidth * 5 / 8;
            var logWidth = contentWidth - portsWidth;
            var bodyHeight = Math.Max(1, available - PanelHeightOverhead);
            var grid = new Grid();
            grid.AddColumn(new GridColumn { Width = portsWidth, Padding = new Padding(0, 0) });
            grid.AddColumn(new GridColumn { Width = PanelGutter, Padding = new Padding(0, 0) });
            grid.AddColumn(new GridColumn { Width = logWidth, Padding = new Padding(0, 0) });
            grid.AddRow(
                Ports(snapshot, now, bodyHeight, portsWidth - PanelWidthOverhead, symbols),
                new Text(" "),
                Log(log, Math.Max(MinimumLogLines, bodyHeight), logWidth - PanelWidthOverhead, symbols));
            return new Rows(banner, header, grid);
        }

        var reservedLogLines = Math.Clamp(log.Count, 1, MinimumLogLines);
        var portsLines = Math.Max(1, available - 2 * PanelHeightOverhead - reservedLogLines);
        var ports = Ports(snapshot, now, portsLines, inner, symbols);
        var logLines = Math.Max(MinimumLogLines, available - HeightOf(ports, renderOptions, width) - PanelHeightOverhead);
        return new Rows(banner, header, ports, Log(log, logLines, inner, symbols));
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

    private static Panel Ports(MirrorSnapshot snapshot, DateTimeOffset now, int height, int width, Symbols symbols)
    {
        IRenderable body = snapshot.Ports.Count == 0
            ? Line([new(snapshot.HasPolled ? "No published TCP ports." : "Waiting for Docker" + symbols.Ellipsis, "grey")], width, symbols)
            : PortTable(snapshot.Ports, now, height, width, symbols);

        return new Panel(body)
        {
            Header = new PanelHeader(Markup.Escape($" Mirrored ports ({snapshot.Ports.Count}) ")),
            Border = BoxBorder.Rounded,
            Expand = true,
        };
    }

    private static Rows PortTable(IReadOnlyList<PortStatus> ports, DateTimeOffset now, int height, int width, Symbols symbols)
    {
        // A compact row keeps the port number and state readable when four columns would not fit.
        var compact = width < 36;
        var remaining = height - (compact ? 0 : 1);
        var visible = new List<PortRow>();

        // Sorted by container then port, with unnamed ports last, so the ports of one container read together.
        var sorted = ports
            .OrderBy(port => port.Container.Length == 0)
            .ThenBy(port => port.Container, StringComparer.OrdinalIgnoreCase)
            .ThenBy(port => port.LocalPort);
        foreach (var port in sorted)
        {
            // Reserve an omission line when more ports follow. If only the summary fits, keep it without its detail.
            var reserved = visible.Count + 1 < ports.Count ? 1 : 0;
            if (remaining <= reserved)
                break;

            remaining--;
            var detail = remaining > reserved ? FailureReason(port) : null;
            if (detail is not null)
                remaining--;
            visible.Add(new PortRow(port, ContainerName(port), $"localhost:{port.LocalPort}", OneLine(port.Remote), detail));
        }

        var lines = new List<IRenderable>();
        if (visible.Count > 0)
        {
            var containerWidth = 0;
            var localWidth = 0;
            var remoteWidth = 0;
            var statusWidth = 0;
            if (!compact)
            {
                containerWidth = Math.Min(MaximumContainerWidth, ColumnWidth("Container", visible.Select(row => row.Container)));
                localWidth = ColumnWidth("Local", visible.Select(row => row.Local));
                remoteWidth = ColumnWidth("Remote", visible.Select(row => row.Remote));
                var available = width - 3 * ColumnGap;
                // Reserve room for the retry countdown before allocating space to long addresses and names.
                var statusReserve = visible.Any(row => row.Port.State != PortState.Open) ? 17 : 6;
                var excess = Math.Max(0, containerWidth + localWidth + remoteWidth + statusReserve - available);
                ReduceWidth(ref containerWidth, 5, ref excess);
                ReduceWidth(ref remoteWidth, 5, ref excess);
                ReduceWidth(ref localWidth, 5, ref excess);
                statusWidth = available - containerWidth - localWidth - remoteWidth;
                lines.Add(Columns("Container", "Local", "Remote", "Status", "grey", "grey", header: true));
            }

            string? previous = null;
            foreach (var row in visible)
            {
                // The container name is shown once per group.
                var container = string.Equals(row.Port.Container, previous, StringComparison.Ordinal) ? "" : row.Container;
                previous = row.Port.Container;
                lines.Add(compact
                    ? CompactPortLine(row.Port, container, now, width, symbols)
                    : Columns(container, row.Local, row.Remote, StatusText(row.Port, now, symbols, statusWidth),
                        row.Container == "-" ? "grey" : null, StatusStyle(row.Port)));
                if (row.Detail is { } detail)
                    lines.Add(Line([new($"{symbols.FailureDetail} {detail}", "red")], width, symbols));
            }

            // Compose the summary columns separately so details can use the full panel width.
            Markup Columns(string container, string local, string remote, string status, string? containerStyle, string statusStyle, bool header = false) =>
                Line([
                    new(PaddedColumn(container, containerWidth, symbols), containerStyle),
                    new(PaddedColumn(local, localWidth, symbols), header ? "grey" : null),
                    new(PaddedColumn(remote, remoteWidth, symbols), header ? "grey" : null),
                    new(status, statusStyle),
                ], width, symbols);
        }

        if (visible.Count < ports.Count)
            lines.Add(Line([new($"+{ports.Count - visible.Count} more ports", "grey")], width, symbols));
        return new Rows(lines);
    }

    private static Markup CompactPortLine(PortStatus port, string name, DateTimeOffset now, int width, Symbols symbols)
    {
        var local = port.LocalPort.ToString(CultureInfo.InvariantCulture) + " ";
        var available = width - local.Length;
        // Reserve a short name prefix and use a shorter state or countdown if the full label would crowd it out.
        var nameReserve = name.Length == 0 ? 0 : 1 + Math.Min(4, CellWidth(name));
        var status = StatusText(port, now, symbols, available - nameReserve);
        var remaining = available - CellWidth(status);
        if (name.Length > 0)
        {
            Shorten(ref name, Math.Max(0, remaining - 1), symbols);
            if (name.Length > 0)
                name += " ";
        }

        return Line([new(local), new(name), new(status, StatusStyle(port))], width, symbols);
    }

    private static string ContainerName(PortStatus port) => port.Container.Length == 0 ? "-" : OneLine(port.Container);

    private static string StatusStyle(PortStatus port) => port.State == PortState.Open ? "green" : "red";

    private static int ColumnWidth(string header, IEnumerable<string> values) => Math.Max(header.Length, values.Max(CellWidth));

    private static void ReduceWidth(ref int width, int minimum, ref int excess)
    {
        var removed = Math.Min(width - minimum, excess);
        width -= removed;
        excess -= removed;
    }

    private static string PaddedColumn(string text, int width, Symbols symbols)
    {
        Shorten(ref text, width, symbols);
        return text + new string(' ', width - CellWidth(text) + ColumnGap);
    }

    private static string StatusText(PortStatus port, DateTimeOffset now, Symbols symbols, int width = int.MaxValue)
    {
        if (port.State == PortState.Open)
            return $"{symbols.Ok} open";

        if (port.RetryAt is not { } retryAt)
            return FitStatus($"{symbols.Failed} starting", "start");

        var seconds = Math.Max(0, (int)Math.Ceiling((retryAt - now).TotalSeconds));
        if (seconds == 0)
            return FitStatus($"{symbols.Failed} retrying", "retry");

        var countdown = seconds.ToString(CultureInfo.InvariantCulture) + "s";
        return FitStatus($"{symbols.Failed} retry in {countdown}", $"{symbols.Failed} {countdown}");

        string FitStatus(string full, string compact) => CellWidth(full) <= width ? full : compact;
    }

    private static string? FailureReason(PortStatus port)
    {
        if (port.State == PortState.Open || string.IsNullOrWhiteSpace(port.Reason))
            return null;

        var reason = OneLine(port.Reason.Trim());
        // OpenSSH puts the bind address first; show the cause first so it survives truncation.
        var separator = reason.StartsWith("bind ", StringComparison.Ordinal) ? reason.IndexOf(": ", StringComparison.Ordinal) : -1;
        return separator > 0 ? $"{reason[(separator + 2)..]} ({reason[..separator]})" : reason;
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
        var texts = parts.Select(part => OneLine(part.Text)).ToArray();
        var overflow = texts.Sum(CellWidth) - width;
        for (var i = 0; i < texts.Length && overflow > 0; i++)
        {
            if (parts[i].Flexible)
                overflow -= Shorten(ref texts[i], Math.Max(1 + symbols.Ellipsis.Length, CellWidth(texts[i]) - overflow), symbols);
        }

        for (var i = texts.Length - 1; i >= 0 && overflow > 0; i--)
            overflow -= Shorten(ref texts[i], Math.Max(0, CellWidth(texts[i]) - overflow), symbols);

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

    private static int CellWidth(string text) => new Segment(text).CellCount();

    /// <summary>Cuts text at a text element boundary to fit the cell budget, and returns the cells removed.</summary>
    private static int Shorten(ref string text, int width, Symbols symbols)
    {
        var before = CellWidth(text);
        if (before <= width)
            return 0;

        var ellipsis = width > symbols.Ellipsis.Length ? symbols.Ellipsis : "";
        var remaining = width - ellipsis.Length;
        var end = 0;
        while (end < text.Length && remaining > 0)
        {
            var length = StringInfo.GetNextTextElementLength(text.AsSpan(end));
            var cells = CellWidth(text.Substring(end, length));
            if (cells > remaining)
                break;
            end += length;
            remaining -= cells;
        }

        text = text[..end] + ellipsis;
        return before - width + remaining;
    }

    /// <summary>The text on one line: line breaks, tabs and other control characters become spaces.</summary>
    private static string OneLine(string text)
    {
        if (!text.Any(c => char.IsControl(c) || c is '\u2028' or '\u2029'))
            return text;

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
            builder.Append(char.IsControl(c) || c is '\u2028' or '\u2029' ? ' ' : c);
        return builder.ToString();
    }

    private static int HeightOf(IRenderable renderable, RenderOptions options, int width) =>
        Segment.SplitLines(renderable.Render(options, width)).Count;

    private static Part Label(string text) => new(text, "grey");

    private readonly record struct Part(string Text, string? Style = null, bool Flexible = false);

    private sealed record PortRow(PortStatus Port, string Container, string Local, string Remote, string? Detail);

    private sealed record Symbols(string Ok, string Failed, string FailureDetail, string Ellipsis);
}
