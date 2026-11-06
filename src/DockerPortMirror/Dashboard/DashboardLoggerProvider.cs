using Microsoft.Extensions.Logging;

namespace DockerPortMirror.Dashboard;

/// <summary>
/// Sends log entries to the <see cref="DashboardLog"/> instead of the console, so nothing writes to the console
/// while the live dashboard is on screen. Levels and filters are the logging builder's, as for the console.
/// </summary>
internal sealed class DashboardLoggerProvider : ILoggerProvider
{
    private readonly DashboardLog _log;
    private readonly bool _includeExceptionDetails;

    /// <param name="log">Where the entries go.</param>
    /// <param name="includeExceptionDetails">Add the stack traces of logged exceptions (with <c>--verbose</c>).</param>
    public DashboardLoggerProvider(DashboardLog log, bool includeExceptionDetails)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
        _includeExceptionDetails = includeExceptionDetails;
    }

    public ILogger CreateLogger(string categoryName) => new DashboardLogger(_log, _includeExceptionDetails);

    public void Dispose()
    {
    }

    private sealed class DashboardLogger : ILogger
    {
        private readonly DashboardLog _log;
        private readonly bool _includeExceptionDetails;

        public DashboardLogger(DashboardLog log, bool includeExceptionDetails)
        {
            _log = log;
            _includeExceptionDetails = includeExceptionDetails;
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            ArgumentNullException.ThrowIfNull(formatter);
            if (!IsEnabled(logLevel))
                return;

            var message = formatter(state, exception);
            if (string.IsNullOrEmpty(message) && exception is null)
                return;

            var text = _includeExceptionDetails && exception is not null
                ? $"{message}{Environment.NewLine}{exception}"
                : message ?? "";
            _log.Add(logLevel, eventId.Id, text);
        }
    }
}
