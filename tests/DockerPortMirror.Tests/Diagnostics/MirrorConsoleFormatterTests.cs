using DockerPortMirror.Diagnostics;
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

    [Fact]
    public void Status_lines_are_printed_as_they_are()
    {
        Assert.Equal(" + localhost:5432 -> mac -> 127.0.0.1:5432" + Environment.NewLine, Format(LogLevel.Information, Log.TunnelOpenedId, " + localhost:5432 -> mac -> 127.0.0.1:5432"));
        Assert.Equal(" ! localhost:3000 failed: x (retry in 1s)" + Environment.NewLine, Format(LogLevel.Warning, Log.TunnelFailedId, " ! localhost:3000 failed: x (retry in 1s)"));
    }

    [Fact]
    public void Warnings_and_errors_get_a_prefix()
    {
        Assert.Equal("WARNING: slow" + Environment.NewLine, Format(LogLevel.Warning, 200, "slow"));
        Assert.Equal("ERROR: broken" + Environment.NewLine, Format(LogLevel.Error, 300, "broken"));
    }

    [Fact]
    public void Colours_wrap_status_lines_when_enabled()
    {
        var text = Format(LogLevel.Information, Log.TunnelOpenedId, " + x", useColors: true);

        Assert.StartsWith("\x1B[32m + x", text, StringComparison.Ordinal);
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
