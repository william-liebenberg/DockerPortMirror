using System.Text.RegularExpressions;
using DockerPortMirror.Cli;
using DockerPortMirror.Dashboard;
using DockerPortMirror.Diagnostics;
using DockerPortMirror.Docker;
using DockerPortMirror.Mirroring;
using DockerPortMirror.Ssh;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using Spectre.Console.Rendering;
using Spectre.Console.Testing;

namespace DockerPortMirror.Tests.Dashboard;

public sealed class DashboardViewTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 10, 41, 30, TimeSpan.Zero);

    private static readonly DockerEndpoint Endpoint = new("remote", "ssh://me@dockerhost", new SshTarget("me@dockerhost"));

    private static PortStatus Open(int port, string container) =>
        new(port, container, $"127.0.0.1:{port}", "me@dockerhost", PortState.Open, null, null);

    private static PortStatus Retrying(int port, string container, string reason, double seconds) =>
        new(port, container, $"127.0.0.1:{port}", "", PortState.Retrying, reason, Now + TimeSpan.FromSeconds(seconds));

    private static LogLine Line(string text, LogLevel level = LogLevel.Information, int eventId = 0) => new(Now, level, eventId, text);

    private static string[] Render(MirrorSnapshot snapshot, IReadOnlyList<LogLine>? log = null, int width = 90, int height = 30, bool unicode = true)
    {
        using var console = new TestConsole().Width(width).Height(height).SupportsUnicode(unicode);
        console.Write(DashboardView.Render(new MirrorOptions(), snapshot, log ?? [], Now, console.Profile));
        return console.Lines.Select(line => line.TrimEnd()).ToArray();
    }

    private static int IndexOf(string[] lines, string text) => Array.FindIndex(lines, line => line.Contains(text, StringComparison.Ordinal));

    [Fact]
    public void Colorless_banner_spells_the_app_name_above_the_header()
    {
        using var expected = new TestConsole().Width(120);
        expected.Write(new FigletText("DockerPortMirror"));
        var figlet = expected.Lines.Select(line => line.TrimEnd()).ToArray();

        using var console = new TestConsole().Width(120).Colors(ColorSystem.NoColors).EmitAnsiSequences();
        console.Write(DashboardView.Render(new MirrorOptions(), MirrorSnapshot.Empty, [], Now, console.Profile));

        var lines = console.Lines.Select(line => line.TrimEnd()).ToArray();
        Assert.Equal(figlet, lines[..figlet.Length]);
        Assert.Contains(AppInfo.Name, lines[figlet.Length], StringComparison.Ordinal);
        Assert.DoesNotContain("\x1b", console.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Banner_uses_multiple_foreground_colors()
    {
        using var console = new TestConsole().Width(120).Colors(ColorSystem.Standard).EmitAnsiSequences();
        console.Write(DashboardView.Render(new MirrorOptions(), MirrorSnapshot.Empty, [], Now, console.Profile));
        var headerIndex = Array.FindIndex(console.Lines.ToArray(), line => line.Contains(AppInfo.Name, StringComparison.Ordinal));
        Assert.True(headerIndex > 0);
        var banner = string.Join('\n', console.Lines.Take(headerIndex));
        var colors = Regex.Matches(banner, @"\x1b\[(?:3[0-7]|9[0-7])m").Select(match => match.Value).Distinct();
        Assert.True(colors.Count() >= 4, banner);
    }

    [Theory]
    [InlineData(20, 5)]
    [InlineData(8, 3)]
    [InlineData(1, 1)]
    public void Narrow_short_terminal_renders_clipped_banner(int width, int height)
    {
        var lines = Render(new MirrorSnapshot(Endpoint, true, null, [Open(5432, "postgres")]), width: width, height: height);

        using var expected = new TestConsole().Width(120);
        expected.Write(new FigletText(AppInfo.Name));
        var clipped = expected.Lines.Select(line => line[..Math.Min(line.Length, width)].TrimEnd()).ToArray();
        Assert.Equal(clipped, lines[..clipped.Length]);
    }

    [Fact]
    public void Header_shows_the_connection()
    {
        var lines = Render(new MirrorSnapshot(Endpoint, true, null, []));
        Assert.Contains(lines, line => line.Contains("Context  remote (ssh://me@dockerhost)", StringComparison.Ordinal) && line.Contains("SSH  me@dockerhost", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Bind  127.0.0.1", StringComparison.Ordinal) && line.Contains("● reachable", StringComparison.Ordinal) && line.Contains("Ctrl+C to stop", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Mirrored ports (0)", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("No published TCP ports.", StringComparison.Ordinal));
    }

    [Fact]
    public void Before_the_first_poll_the_header_says_so()
    {
        var lines = Render(MirrorSnapshot.Empty);
        Assert.Contains(lines, line => line.Contains("Context  resolving…", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Docker  connecting…", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("Waiting for Docker…", StringComparison.Ordinal));
    }

    [Fact]
    public void Ports_are_grouped_by_container_and_sorted()
    {
        var lines = Render(new MirrorSnapshot(Endpoint, true, null, [Open(9000, ""), Open(8443, "web"), Open(5432, "postgres"), Open(8080, "web"), Open(3000, "api")]));

        Assert.Contains(lines, line => line.Contains("Mirrored ports (5)", StringComparison.Ordinal));
        int[] order = [IndexOf(lines, "localhost:3000"), IndexOf(lines, "localhost:5432"), IndexOf(lines, "localhost:8080"), IndexOf(lines, "localhost:8443"), IndexOf(lines, "localhost:9000")];
        Assert.All(order, index => Assert.True(index >= 0));
        Assert.Equal(order.Order(), order);
        Assert.Matches(@"│ api\s+localhost:3000\s+127\.0\.0\.1:3000\s+● open", lines[order[0]]);
        Assert.Matches(@"│ web\s+localhost:8080", lines[order[2]]);
        Assert.Matches(@"│\s+localhost:8443", lines[order[3]]);
        Assert.Matches(@"│ -\s+localhost:9000", lines[order[4]]);
    }

    [Theory]
    [InlineData(4, "✗ retry in 4s")]
    [InlineData(3.2, "✗ retry in 4s")]
    [InlineData(-1, "✗ retrying")]
    public void Retrying_port_counts_down(double seconds, string expected)
    {
        var lines = Render(new MirrorSnapshot(Endpoint, true, null, [Retrying(3000, "api", "bind: Address already in use", seconds)]));

        Assert.Contains(lines, line => line.Contains(expected, StringComparison.Ordinal));
    }

    [Fact]
    public void Docker_error_is_shown_in_red()
    {
        const string error = "Unable to query Docker context 'remote'. Network is unreachable";
        using var console = new TestConsole().Width(120).Height(30).SupportsUnicode(true).Colors(ColorSystem.Standard).EmitAnsiSequences();

        console.Write(DashboardView.Render(new MirrorOptions(), new MirrorSnapshot(Endpoint, true, error, [Open(5432, "postgres")]), [], Now, console.Profile));

        Assert.Matches(new Regex(@"\x1b\[(31|91)m✗ Unable to query Docker context 'remote'\. Network is unreachable"), console.Output);
        Assert.Contains("postgres", console.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Ctrl+C", console.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(80)]
    [InlineData(91)]
    [InlineData(120)]
    public void Wide_panels_start_on_the_same_row_after_the_connection(int width)
    {
        var lines = Render(new MirrorSnapshot(Endpoint, true, null, []), width: width);

        var ports = IndexOf(lines, "Mirrored ports (0)");
        var gutter = (width - 1) * 5 / 8;
        Assert.Equal(IndexOf(lines, "Docker  ● reachable") + 2, ports);
        Assert.Equal(ports, IndexOf(lines, "Log"));
        Assert.True(lines[ports].IndexOf("Mirrored ports", StringComparison.Ordinal) < lines[ports].IndexOf("Log", StringComparison.Ordinal));
        Assert.Equal(width, lines[ports].Length);
        Assert.Equal("╮ ╭", lines[ports][(gutter - 1)..(gutter + 2)]);
        Assert.Equal("│ │", lines[ports + 1][(gutter - 1)..(gutter + 2)]);
        Assert.Contains(lines, line => line.Contains("No messages yet.", StringComparison.Ordinal));
    }

    [Fact]
    public void Narrow_panels_are_stacked()
    {
        var lines = Render(new MirrorSnapshot(Endpoint, true, null, [Open(5432, "postgres")]), [Line("latest log")], width: 79);

        Assert.True(IndexOf(lines, "Mirrored ports (1)") < IndexOf(lines, "localhost:5432"));
        Assert.True(IndexOf(lines, "localhost:5432") < IndexOf(lines, "Log"));
        Assert.True(IndexOf(lines, "Log") < IndexOf(lines, "latest log"));
    }

    [Theory]
    [InlineData(35, true, "✗ retry in 60s", "…")]
    [InlineData(35, false, "x retry in 60s", "...")]
    [InlineData(20, true, "✗ 60s", "")]
    [InlineData(20, false, "x 60s", "")]
    public void Compact_retry_rows_keep_the_container_and_state_before_the_reason(int width, bool unicode, string status, string ellipsis)
    {
        const string reason = "Address already in use on the local machine";
        var lines = Render(new MirrorSnapshot(Endpoint, true, null,
            [Retrying(65534, "web", reason, 60), Retrying(65535, "web", reason, 60)]), width: width, unicode: unicode);

        var first = IndexOf(lines, "65534");
        var second = IndexOf(lines, "65535");
        Assert.True(first >= 0);
        Assert.Equal(first + 1, second);
        Assert.Contains($"65534 web {status}", lines[first], StringComparison.Ordinal);
        Assert.Contains($"65535 {status}", lines[second], StringComparison.Ordinal);
        Assert.DoesNotContain(reason, lines[first], StringComparison.Ordinal);
        if (ellipsis.Length > 0)
        {
            Assert.Contains(status + ": A", lines[first], StringComparison.Ordinal);
            Assert.Contains(ellipsis, lines[first], StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(true, true, "wor… start", "✗ starting")]
    [InlineData(true, false, "w... start", "x starting")]
    [InlineData(false, true, "wor… retry", "✗ retrying")]
    [InlineData(false, false, "w... retry", "x retrying")]
    public void Compact_starting_and_due_retry_rows_keep_the_container_prefix(bool starting, bool unicode, string firstRow, string secondStatus)
    {
        var port = Retrying(65534, "worker", "Address already in use", 0);
        if (starting)
            port = port with { RetryAt = null, Reason = null };

        var lines = Render(new MirrorSnapshot(Endpoint, true, null, [port, port with { LocalPort = 65535 }]), width: 20, unicode: unicode);

        var first = IndexOf(lines, "65534");
        var second = IndexOf(lines, "65535");
        Assert.True(first >= 0);
        Assert.Equal(first + 1, second);
        Assert.Contains($"65534 {firstRow}", lines[first], StringComparison.Ordinal);
        Assert.Contains($"65535 {secondStatus}", lines[second], StringComparison.Ordinal);
    }

    [Fact]
    public void Log_keeps_the_newest_lines_that_fit()
    {
        var log = Enumerable.Range(0, 100).Select(i => Line($"message {i:00}")).ToArray();

        var lines = Render(new MirrorSnapshot(Endpoint, true, null, [Open(5432, "postgres")]), log, height: 22);

        // Banner (6) + connection (4) + content borders (2) + 9 log lines = 21.
        Assert.Equal(21, lines.Length);
        Assert.Equal(Enumerable.Range(91, 9).Select(i => $"message {i}"), Messages(lines));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void Ports_and_logs_have_independent_height_budgets(int logCount)
    {
        var ports = Enumerable.Range(3000, 20).Reverse().Select(i => Open(i, "api")).ToArray();
        var log = Enumerable.Range(0, logCount).Select(i => Line($"message {i:00}")).ToArray();
        var lines = Render(new MirrorSnapshot(Endpoint, true, null, ports), log, height: 22);

        Assert.Equal(21, lines.Length);
        Assert.Equal(IndexOf(lines, "Mirrored ports (20)"), IndexOf(lines, "Log"));
        Assert.Equal(Enumerable.Range(3000, 7).Select(i => $"localhost:{i}"), lines.SelectMany(line => Regex.Matches(line, @"localhost:\d+").Select(match => match.Value)));
        Assert.Contains(lines, line => line.Contains("+13 more ports", StringComparison.Ordinal));
        Assert.Equal(logCount == 0 ? [] : Enumerable.Range(91, 9).Select(i => $"message {i}"), Messages(lines));
    }

    private static IEnumerable<string> Messages(string[] lines) =>
        lines.SelectMany(line => Regex.Matches(line, @"message \d+").Select(match => match.Value));

    [Theory]
    [InlineData(79)]
    [InlineData(90)]
    public void Short_terminal_keeps_the_newest_log_lines_in_the_rendered_frame(int width)
    {
        var log = Enumerable.Range(0, 10).Select(i => Line($"message {i}")).ToArray();

        var lines = Render(new MirrorSnapshot(Endpoint, true, null, [Open(5432, "postgres")]), log, width: width, height: 8);

        Assert.Equal(Enumerable.Range(log.Length - DashboardView.MinimumLogLines, DashboardView.MinimumLogLines).Select(i => $"message {i}"), Messages(lines));
    }

    [Fact]
    public void Log_lines_follow_the_console_colours_and_prefixes()
    {
        var time = Now.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var lines = Render(
            new MirrorSnapshot(Endpoint, true, null, []),
            [
                Line(" + localhost:5432 -> me@dockerhost -> 127.0.0.1:5432 (postgres)", eventId: Log.TunnelOpenedId),
                Line("Unable to query Docker context 'remote'.", LogLevel.Warning, 200),
                Line("broken", LogLevel.Error, 300),
                new LogLine(Now, LogLevel.Error, 300, "   at Somewhere()", IsContinuation: true),
            ], width: 220);

        Assert.Contains(lines, line => line.Contains($"│ {time}  + localhost:5432 -> me@dockerhost -> 127.0.0.1:5432 (postgres) ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains($"│ {time}  WARNING: Unable to query Docker context 'remote'. ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains($"│ {time}  ERROR: broken ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("│ " + new string(' ', 10) + "   at Somewhere()", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true, "…", "✗")]
    [InlineData(false, "...", "x")]
    public void Long_lines_are_cut_to_their_panel_width(bool unicode, string ellipsis, string failed)
    {
        var port = Retrying(65535, "frontend-with-a-long-container-name", "Address already in use", 4) with
        {
            Remote = "[2001:db8:85a3:0000:0000:8a2e:0370:7334]:65535",
        };
        var lines = Render(
            new MirrorSnapshot(Endpoint, true, null, [port]),
            [Line("bind " + new string('a', 100) + "\nshould not be a new row", LogLevel.Warning, 200)],
            width: 80, unicode: unicode);

        Assert.Equal(14, lines.Length);
        Assert.All(lines, line => Assert.True(line.Length <= 80, line));
        const int gutter = (80 - 1) * 5 / 8;
        var portsRow = lines[IndexOf(lines, "localhost:65535")];
        Assert.Contains(failed + " retry in 4s", portsRow[..gutter], StringComparison.Ordinal);
        Assert.Contains(ellipsis, portsRow[..gutter], StringComparison.Ordinal);
        var logRow = lines[IndexOf(lines, "WARNING: bind")];
        Assert.Contains(ellipsis, logRow[(gutter + 1)..], StringComparison.Ordinal);
    }

    [Fact]
    public void Wide_unicode_text_fits_in_its_panel_without_wrapping()
    {
        var text = string.Concat(Enumerable.Repeat("港\uD83D\uDEA2", 40));
        var lines = Render(new MirrorSnapshot(Endpoint, true, null, [Open(5432, text)]), [Line(text)], width: 80);

        Assert.Equal(14, lines.Length);
        Assert.All(lines, line => Assert.True(new Segment(line).CellCount() <= 80, line));
        Assert.Equal(1, lines.Count(line => line.Contains("localhost:5432", StringComparison.Ordinal)));
        Assert.Equal(2, lines.Count(line => line.Contains('港') && line.Contains('…')));
    }

    [Fact]
    public void Ascii_terminal_gets_ascii_symbols()
    {
        var lines = Render(new MirrorSnapshot(Endpoint, true, null, [Open(5432, "postgres"), Retrying(3000, "api", "in use", 2)]), unicode: false);

        Assert.Contains(lines, line => line.Contains("* open", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("x retry in 2s", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("* reachable", StringComparison.Ordinal));
    }
}
