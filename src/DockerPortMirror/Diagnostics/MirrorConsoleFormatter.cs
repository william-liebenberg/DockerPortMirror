using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace DockerPortMirror.Diagnostics;

internal sealed class MirrorConsoleFormatterOptions : ConsoleFormatterOptions
{
    public bool UseColors { get; set; } = true;

    /// <summary>Print stack traces of logged exceptions (with <c>--verbose</c>).</summary>
    public bool IncludeExceptionDetails { get; set; }
}

/// <summary>
/// Prints log entries as the tool's plain console output: messages without category, status lines
/// (<c>+</c>, <c>-</c>, <c>!</c>) in colour, and a <c>WARNING:</c> or <c>ERROR:</c> prefix for other problems.
/// </summary>
internal sealed class MirrorConsoleFormatter : ConsoleFormatter, IDisposable
{
    public const string FormatterName = "dockerportmirror";

    private const string Green = "\x1B[32m";
    private const string Yellow = "\x1B[33m";
    private const string Red = "\x1B[31m";
    private const string Gray = "\x1B[37m";
    private const string Reset = "\x1B[39m\x1B[22m";

    private readonly IDisposable? _reloadToken;
    private MirrorConsoleFormatterOptions _options;

    public MirrorConsoleFormatter(IOptionsMonitor<MirrorConsoleFormatterOptions> options)
        : base(FormatterName)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.CurrentValue;
        _reloadToken = options.OnChange(updated => _options = updated);
    }

    public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
    {
        ArgumentNullException.ThrowIfNull(textWriter);
        var message = logEntry.Formatter(logEntry.State, logEntry.Exception);
        if (message is null && logEntry.Exception is null)
            return;

        var (prefix, color) = Style(logEntry.EventId.Id, logEntry.LogLevel);
        var useColor = _options.UseColors && color is not null;
        if (useColor)
            textWriter.Write(color);
        textWriter.Write(prefix);
        textWriter.Write(message);
        if (useColor)
            textWriter.Write(Reset);
        textWriter.Write(Environment.NewLine);

        if (_options.IncludeExceptionDetails && logEntry.Exception is not null)
            textWriter.WriteLine(logEntry.Exception.ToString());
    }

    public void Dispose() => _reloadToken?.Dispose();

    internal static (string Prefix, string? Color) Style(int eventId, LogLevel level) => eventId switch
    {
        Log.TunnelOpenedId => ("", Green),
        Log.TunnelClosedId => ("", Yellow),
        Log.TunnelFailedId => ("", Red),
        _ => level switch
        {
            LogLevel.Trace or LogLevel.Debug => ("", Gray),
            LogLevel.Warning => ("WARNING: ", Yellow),
            LogLevel.Error or LogLevel.Critical => ("ERROR: ", Red),
            _ => ("", null),
        },
    };
}

internal static class MirrorConsoleLoggingExtensions
{
    /// <summary>Logs to the console in the tool's own format; <paramref name="verbose"/> adds diagnostics and stack traces.</summary>
    public static ILoggingBuilder AddMirrorConsole(this ILoggingBuilder logging, bool verbose)
    {
        logging.ClearProviders();
        logging.AddMirrorFilters(verbose);
        logging.AddConsole(options =>
        {
            options.FormatterName = MirrorConsoleFormatter.FormatterName;
            options.LogToStandardErrorThreshold = LogLevel.Warning;
        });
        logging.AddConsoleFormatter<MirrorConsoleFormatter, MirrorConsoleFormatterOptions>(options =>
        {
            options.IncludeExceptionDetails = verbose;
            options.UseColors = !Console.IsOutputRedirected
                && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        });
        return logging;
    }

    /// <summary>The levels the tool shows, whatever the output: <paramref name="verbose"/> adds diagnostics.</summary>
    public static ILoggingBuilder AddMirrorFilters(this ILoggingBuilder logging, bool verbose)
    {
        logging.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
        logging.AddFilter("Microsoft", verbose ? LogLevel.Information : LogLevel.Warning);
        logging.AddFilter("System", LogLevel.Warning);

        // The host reports start-up failures that the tool reports itself, with a clearer message.
        logging.AddFilter("Microsoft.Extensions.Hosting", verbose ? LogLevel.Information : LogLevel.Critical);
        return logging;
    }
}
