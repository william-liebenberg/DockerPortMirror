using DockerPortMirror.Dashboard;
using DockerPortMirror.Diagnostics;
using DockerPortMirror.Tests.Fakes;
using Microsoft.Extensions.Logging;

namespace DockerPortMirror.Tests.Dashboard;

public sealed class DashboardLogTests
{
    [Fact]
    public void Only_the_newest_lines_are_kept()
    {
        var log = new DashboardLog(new ManualTimeProvider());

        for (var i = 0; i < DashboardLog.DefaultCapacity + 20; i++)
            log.Add(LogLevel.Information, 1, $"line {i}");

        var lines = log.Lines();
        Assert.Equal(DashboardLog.DefaultCapacity, lines.Count);
        Assert.Equal("line 20", lines[0].Text);
        Assert.Equal($"line {DashboardLog.DefaultCapacity + 19}", lines[^1].Text);
    }

    [Fact]
    public void Message_of_several_lines_gives_continuation_lines()
    {
        var time = new ManualTimeProvider();
        var log = new DashboardLog(time);

        log.Add(LogLevel.Error, 300, "first\r\nsecond");

        Assert.Equal(
            [new LogLine(time.GetUtcNow(), LogLevel.Error, 300, "first"), new LogLine(time.GetUtcNow(), LogLevel.Error, 300, "second", IsContinuation: true)],
            log.Lines());
    }

    [Fact]
    public void Logger_keeps_level_and_event_id_and_applies_the_filters()
    {
        var log = new DashboardLog(new ManualTimeProvider());
        using (var factory = LoggerFactory.Create(logging => logging.AddMirrorDashboard(log, verbose: false)))
        {
            var logger = factory.CreateLogger("DockerPortMirror.Test");
            Log.PollFailed(logger, "slow");
            Log.CommandRunning(logger, "docker", "ps");
            Log.FatalError(factory.CreateLogger("Microsoft.Extensions.Hosting.Internal.Host"), "host noise");
        }

        var line = Assert.Single(log.Lines());
        Assert.Equal((LogLevel.Warning, 200, "slow"), (line.Level, line.EventId, line.Text));
    }

    [Fact]
    public void Verbose_adds_diagnostics_and_stack_traces()
    {
        var exception = new InvalidOperationException("boom");
        var quiet = new DashboardLog(new ManualTimeProvider());
        var verbose = new DashboardLog(new ManualTimeProvider());
        Write(quiet, verbose: false);
        Write(verbose, verbose: true);

        Assert.Equal(["Unexpected error: boom"], quiet.Lines().Select(line => line.Text));
        Assert.Contains(verbose.Lines(), line => line.Text == "Running: docker ps");
        Assert.Contains(verbose.Lines(), line => line.IsContinuation && line.Text.Contains("InvalidOperationException", StringComparison.Ordinal));

        void Write(DashboardLog log, bool verbose)
        {
            using var factory = LoggerFactory.Create(logging => logging.AddMirrorDashboard(log, verbose));
            var logger = factory.CreateLogger("DockerPortMirror.Test");
            Log.CommandRunning(logger, "docker", "ps");
            Log.UnexpectedError(logger, "boom", exception);
        }
    }
}
