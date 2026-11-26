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
    [InlineData(4, "✗ retry in 4s: Address already in use")]
    [InlineData(3.2, "✗ retry in 4s: Address already in use")]
    [InlineData(-1, "✗ retrying: Address already in use")]
    public void Retrying_port_counts_down(double seconds, string expected)
    {
        var lines = Render(new MirrorSnapshot(Endpoint, true, null, [Retrying(3000, "api", "Address already in use", seconds)]));

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

    [Fact]
    public void Log_keeps_the_newest_lines_that_fit()
    {
        var log = Enumerable.Range(0, 100).Select(i => Line($"message {i:00}")).ToArray();

        var lines = Render(new MirrorSnapshot(Endpoint, true, null, [Open(5432, "postgres")]), log, height: 22);

        // Banner (6) + header (4) + ports (4) + log (2 + 5) = 21: one line below the terminal height.
        Assert.Equal(21, lines.Length);
        Assert.Contains(lines, line => line.Contains("message 99", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("message 95", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("message 94", StringComparison.Ordinal));
    }

    [Fact]
    public void Small_terminal_keeps_a_few_log_lines()
    {
        var log = Enumerable.Range(0, 10).Select(i => Line($"message {i}")).ToArray();

        var lines = Render(new MirrorSnapshot(Endpoint, true, null, [Open(5432, "postgres")]), log, height: 8);

        Assert.Equal(DashboardView.MinimumLogLines, lines.Count(line => line.Contains("message", StringComparison.Ordinal)));
        Assert.Contains(lines, line => line.Contains("message 9", StringComparison.Ordinal));
    }

    [Fact]
    public void Log_lines_preserve_symbols_and_follow_the_console_prefixes()
    {
        var time = Now.ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
        var lines = Render(
            new MirrorSnapshot(Endpoint, true, null, []),
            [
                Line(" ➕ localhost:5432 ➡️ mac+alias ➡️ 127.0.0.1:5432 (db-*)", eventId: Log.TunnelOpenedId),
                Line(" ➖ localhost:5432 (db-*)", eventId: Log.TunnelClosedId),
                Line("Docker diagnostic: * -> + -", LogLevel.Warning, 200),
                Line("broken", LogLevel.Error, 300),
                new LogLine(Now, LogLevel.Error, 300, "   at Somewhere()", IsContinuation: true),
            ]);

        Assert.Contains(lines, line => line.StartsWith($"│ {time}  ➕ localhost:5432 ➡️ mac+alias ➡️ 127.0.0.1:5432 (db-*) ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith($"│ {time}  ➖ localhost:5432 (db-*) ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith($"│ {time}  WARNING: Docker diagnostic: * -> + - ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith($"│ {time}  ERROR: broken ", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("│ " + new string(' ', 10) + "   at Somewhere()", StringComparison.Ordinal));
    }

    [Fact]
    public void Tunnel_log_colours_follow_event_ids()
    {
        using var console = new TestConsole().Width(90).Height(30).Colors(ColorSystem.Standard).EmitAnsiSequences();
        LogLine[] log =
        [
            Line(" ➕ opened", eventId: Log.TunnelOpenedId),
            Line(" ➖ closed", eventId: Log.TunnelClosedId),
            Line(" ! failed", LogLevel.Warning, Log.TunnelFailedId),
        ];
        console.Write(DashboardView.Render(new MirrorOptions(), MirrorSnapshot.Empty, log, Now, console.Profile));

        Assert.Matches(@"\x1b\[(32|92)m➕ opened", console.Output);
        Assert.Matches(@"\x1b\[(33|93)m➖ closed", console.Output);
        Assert.Matches(@"\x1b\[(31|91)m! failed", console.Output);
    }

    [Theory]
    [InlineData(21, "➕ a …")]
    [InlineData(22, "➕ a ➡️…")]
    public void Narrow_log_clips_whole_emoji_without_wrapping(int width, string expected)
    {
        var lines = Render(MirrorSnapshot.Empty, [Line(" ➕ a ➡️ tail", eventId: Log.TunnelOpenedId)], width: width);
        var row = Assert.Single(lines, line => line.Contains('➕'));

        Assert.Contains(expected, row, StringComparison.Ordinal);
        Assert.DoesNotContain(lines, line => line.Contains("tail", StringComparison.Ordinal));
        Assert.All(lines, line => Assert.True(new Segment(line).CellCount() <= width, line));
    }

    [Fact]
    public void Long_lines_are_cut_with_an_ellipsis_instead_of_wrapping()
    {
        var reason = string.Join(" ", Enumerable.Repeat("bind: Address already in use", 10));
        var lines = Render(
            new MirrorSnapshot(Endpoint, true, null, [Retrying(3000, "api", reason, 4), Open(5432, "postgres")]),
            [Line(reason, LogLevel.Warning, 200)],
            width: 80);

        Assert.All(lines, line => Assert.True(line.Length <= 80, line));
        Assert.Contains(lines, line => line.Contains("✗ retry in 4s: bind", StringComparison.Ordinal) && line.EndsWith("… │", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("WARNING: bind", StringComparison.Ordinal) && line.EndsWith("… │", StringComparison.Ordinal));
        Assert.Equal(1, lines.Count(line => line.Contains("localhost:3000", StringComparison.Ordinal)));
    }

    [Fact]
    public void Non_unicode_profile_uses_green_circle_for_healthy_status()
    {
        var lines = Render(new MirrorSnapshot(Endpoint, true, null, [Open(5432, "postgres"), Retrying(3000, "api", "in use", 2)]), unicode: false);

        Assert.Contains(lines, line => line.Contains("🟢 open", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("x retry in 2s: in use", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("🟢 reachable", StringComparison.Ordinal));
    }

    [Fact]
    public void Non_unicode_profile_keeps_ascii_ellipsis()
    {
        var lines = Render(MirrorSnapshot.Empty, unicode: false);

        Assert.Contains(lines, line => line.Contains("Docker  connecting...", StringComparison.Ordinal));
    }
}
