using Microsoft.Extensions.Logging;

namespace DockerPortMirror.Tests.Fakes;

internal sealed record LogRecord(LogLevel Level, EventId EventId, string Message);

internal sealed class RecordingLogger<T> : ILogger<T>
{
    public List<LogRecord> Records { get; } = [];

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        Records.Add(new LogRecord(logLevel, eventId, formatter(state, exception)));
}
