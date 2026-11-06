using Microsoft.Extensions.Logging;

namespace DockerPortMirror.Diagnostics;

/// <summary>Every message of the tool, as source-generated logging methods.</summary>
internal static partial class Log
{
    // Status lines: the formatter prints them as they are, in colour, without a level prefix.
    public const int TunnelOpenedId = 100;
    public const int TunnelClosedId = 101;
    public const int TunnelFailedId = 102;

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "{Name} {Version}")]
    public static partial void Banner(ILogger logger, string name, string version);

    [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Docker context : {Context}  ({Endpoint})")]
    public static partial void BannerContext(ILogger logger, string context, string endpoint);

    [LoggerMessage(EventId = 3, Level = LogLevel.Information, Message = "SSH target     : {SshTarget}")]
    public static partial void BannerSshTarget(ILogger logger, string sshTarget);

    [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Local bind     : {BindAddress}   Poll: {PollSeconds}s")]
    public static partial void BannerSettings(ILogger logger, string bindAddress, int pollSeconds);

    [LoggerMessage(EventId = 5, Level = LogLevel.Information, Message = "Press Ctrl+C to stop.")]
    public static partial void BannerHint(ILogger logger);

    [LoggerMessage(EventId = 6, Level = LogLevel.Information, Message = "Stopping tunnels...")]
    public static partial void Stopping(ILogger logger);

    // The container name follows the port as " (name)", or nothing when it is unknown.
    public static void TunnelOpened(ILogger logger, int localPort, string container, string via, string remoteHost, int remotePort)
    {
        var owner = Owner(container);
        TunnelOpenedCore(logger, localPort, via, remoteHost, remotePort, owner);
    }

    public static void TunnelClosed(ILogger logger, int localPort, string container)
    {
        var owner = Owner(container);
        TunnelClosedCore(logger, localPort, owner);
    }

    public static void TunnelFailed(ILogger logger, int localPort, string container, string reason, int retrySeconds)
    {
        var owner = Owner(container);
        TunnelFailedCore(logger, localPort, owner, reason, retrySeconds);
    }

    public static void TunnelStillFailing(ILogger logger, int localPort, string container, int failures, string reason, int retrySeconds)
    {
        var owner = Owner(container);
        TunnelStillFailingCore(logger, localPort, owner, failures, reason, retrySeconds);
    }

    [LoggerMessage(EventId = TunnelOpenedId, Level = LogLevel.Information, Message = " + localhost:{LocalPort} -> {Via} -> {RemoteHost}:{RemotePort}{Owner}")]
    private static partial void TunnelOpenedCore(ILogger logger, int localPort, string via, string remoteHost, int remotePort, string owner);

    [LoggerMessage(EventId = TunnelClosedId, Level = LogLevel.Information, Message = " - localhost:{LocalPort}{Owner}")]
    private static partial void TunnelClosedCore(ILogger logger, int localPort, string owner);

    [LoggerMessage(EventId = TunnelFailedId, Level = LogLevel.Warning, Message = " ! localhost:{LocalPort}{Owner} failed: {Reason} (retry in {RetrySeconds}s)")]
    private static partial void TunnelFailedCore(ILogger logger, int localPort, string owner, string reason, int retrySeconds);

    [LoggerMessage(EventId = 103, Level = LogLevel.Debug, Message = "localhost:{LocalPort}{Owner} failed again ({Failures} times): {Reason} (retry in {RetrySeconds}s)")]
    private static partial void TunnelStillFailingCore(ILogger logger, int localPort, string owner, int failures, string reason, int retrySeconds);

    private static string Owner(string container) => string.IsNullOrEmpty(container) ? "" : $" ({container})";

    [LoggerMessage(EventId = 104, Level = LogLevel.Debug, Message = "ssh[{LocalPort}]: {Line}")]
    public static partial void SshOutput(ILogger logger, int localPort, string line);

    [LoggerMessage(EventId = 105, Level = LogLevel.Debug, Message = "Starting: ssh {Arguments}")]
    public static partial void SshStarting(ILogger logger, string arguments);

    [LoggerMessage(EventId = 200, Level = LogLevel.Warning, Message = "{Message}")]
    public static partial void PollFailed(ILogger logger, string message);

    [LoggerMessage(EventId = 201, Level = LogLevel.Debug, Message = "Still failing: {Message}")]
    public static partial void PollStillFailing(ILogger logger, string message);

    [LoggerMessage(EventId = 202, Level = LogLevel.Information, Message = "Docker is reachable again.")]
    public static partial void PollRecovered(ILogger logger);

    [LoggerMessage(EventId = 203, Level = LogLevel.Debug, Message = "Running: {FileName} {Arguments}")]
    public static partial void CommandRunning(ILogger logger, string fileName, string arguments);

    [LoggerMessage(EventId = 300, Level = LogLevel.Error, Message = "{Message}")]
    public static partial void FatalError(ILogger logger, string message);

    [LoggerMessage(EventId = 301, Level = LogLevel.Critical, Message = "Unexpected error: {Message}")]
    public static partial void UnexpectedError(ILogger logger, string message, Exception? exception);

    [LoggerMessage(EventId = 302, Level = LogLevel.Warning, Message = "Child processes cannot be tied to this process: {Reason}")]
    public static partial void ChildProcessTrackingUnavailable(ILogger logger, string reason);
}
