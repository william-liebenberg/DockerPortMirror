using Microsoft.Extensions.Logging;

namespace DockerPortMirror.Dashboard;

/// <summary>One line of the log panel. A message of several lines gives several entries.</summary>
/// <param name="IsContinuation">Whether the line continues the message of the line before it (a stack trace).</param>
internal sealed record LogLine(DateTimeOffset Time, LogLevel Level, int EventId, string Text, bool IsContinuation = false);

/// <summary>The latest log lines, for the log panel of the dashboard. Thread-safe and bounded.</summary>
internal sealed class DashboardLog
{
    public const int DefaultCapacity = 500;

    private readonly TimeProvider _timeProvider;
    private readonly int _capacity;
    private readonly Queue<LogLine> _lines;
    private readonly Lock _gate = new();

    public DashboardLog(TimeProvider timeProvider, int capacity = DefaultCapacity)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _timeProvider = timeProvider;
        _capacity = capacity;
        _lines = new Queue<LogLine>(capacity);
    }

    public void Add(LogLevel level, int eventId, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var time = _timeProvider.GetUtcNow();
        var lines = text.Split('\n');
        lock (_gate)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (_lines.Count == _capacity)
                    _lines.Dequeue();
                _lines.Enqueue(new LogLine(time, level, eventId, lines[i].TrimEnd('\r'), IsContinuation: i > 0));
            }
        }
    }

    /// <summary>The kept lines, oldest first.</summary>
    public IReadOnlyList<LogLine> Lines()
    {
        lock (_gate)
            return _lines.ToArray();
    }
}
