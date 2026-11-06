using System.ComponentModel;
using System.Diagnostics;
using DockerPortMirror.Diagnostics;
using Microsoft.Extensions.Logging;

namespace DockerPortMirror.Processes;

internal sealed class ProcessRunner : IProcessRunner
{
    private readonly IChildProcessTracker _tracker;
    private readonly ILogger<ProcessRunner> _logger;

    public ProcessRunner(IChildProcessTracker tracker, ILogger<ProcessRunner> logger)
    {
        _tracker = tracker;
        _logger = logger;
    }

    public async Task<ProcessResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var commandLine = string.Join(' ', arguments);
        Log.CommandRunning(_logger, fileName, commandLine);
        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new DockerPortMirrorException(
                $"Unable to start '{fileName}'. Ensure it is installed and available on PATH.", ex);
        }

        _tracker.Track(process);
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            cancellationToken.ThrowIfCancellationRequested();
            throw new DockerPortMirrorException(
                $"'{fileName} {commandLine}' did not finish within {timeout.TotalSeconds:0.#}s and was stopped.");
        }

        return new ProcessResult(
            process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(milliseconds: 3000);
        }
        catch (InvalidOperationException)
        {
            // The process exited between the timeout and the kill.
        }
        catch (Win32Exception)
        {
            // Best effort: the process could not be killed (access denied or already gone).
        }
    }
}
