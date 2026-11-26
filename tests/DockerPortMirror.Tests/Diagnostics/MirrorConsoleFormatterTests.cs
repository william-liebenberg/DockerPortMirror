using DockerPortMirror.Diagnostics;
using DockerPortMirror.Tests.Fakes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DockerPortMirror.Tests.Diagnostics;

public sealed class MirrorConsoleFormatterTests
{
    private static string Format(LogLevel level, int eventId, string message, Exception? exception = null, bool useColors = false, bool includeExceptionDetails = false)
    {
        var options = new StaticOptionsMonitor(new MirrorConsoleFormatterOptions { UseColors = useColors, IncludeExceptionDetails = includeExceptionDetails });
        using var formatter = new MirrorConsoleFormatter(options);
        using var writer = new StringWriter();
        var entry = new LogEntry<string>(level, "category", new EventId(eventId), message, exception, (state, _) => state);
        formatter.Write(entry, null, writer);
        return writer.ToString();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Status_lines_keep_their_symbols_and_dynamic_text(bool useColors)
    {
        var logger = new RecordingLogger<MirrorConsoleFormatterTests>();
        Log.TunnelOpened(logger, 5432, "db-*", "mac+alias", "remote->host", 5432);
        Log.TunnelClosed(logger, 5432, "db-*");
        Log.TunnelFailed(logger, 3000, "", "bind * -> + -", 1);

        var output = logger.Records.Select(record => Format(record.Level, record.EventId.Id, record.Message, useColors: useColors));
        string Styled(string message, int color) => (useColors ? $"\x1B[{color}m{message}\x1B[39m\x1B[22m" : message) + Environment.NewLine;
        Assert.Equal([
            Styled(" ➕ localhost:5432 ➡️ mac+alias ➡️ remote->host:5432 (db-*)", 32),
            Styled(" ➖ localhost:5432 (db-*)", 33),
            Styled(" ! localhost:3000 failed: bind * -> + - (retry in 1s)", 31),
        ], output);
    }

    [Fact]
    public void Warnings_and_errors_get_a_prefix()
    {
        Assert.Equal("WARNING: slow * -> + -" + Environment.NewLine, Format(LogLevel.Warning, 200, "slow * -> + -"));
        Assert.Equal("ERROR: broken" + Environment.NewLine, Format(LogLevel.Error, 300, "broken"));
    }

    [Fact]
    public void Stack_traces_only_appear_when_requested()
    {
        var exception = new InvalidOperationException("boom");

        Assert.DoesNotContain("InvalidOperationException", Format(LogLevel.Critical, 301, "Unexpected error: boom", exception), StringComparison.Ordinal);
        Assert.Contains("InvalidOperationException", Format(LogLevel.Critical, 301, "Unexpected error: boom", exception, includeExceptionDetails: true), StringComparison.Ordinal);
    }

    private sealed class StaticOptionsMonitor(MirrorConsoleFormatterOptions value) : IOptionsMonitor<MirrorConsoleFormatterOptions>
    {
        public MirrorConsoleFormatterOptions CurrentValue => value;

        public MirrorConsoleFormatterOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<MirrorConsoleFormatterOptions, string?> listener) => null;
    }
}
