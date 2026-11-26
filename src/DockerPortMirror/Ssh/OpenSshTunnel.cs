using System.ComponentModel;
using System.Diagnostics;
using DockerPortMirror.Diagnostics;
using DockerPortMirror.Mirroring;
using Microsoft.Extensions.Logging;

namespace DockerPortMirror.Ssh;

/// <summary>One <c>ssh -N -L</c> process. Its standard error is streamed to the logger and to a bounded buffer.</summary>
internal sealed class OpenSshTunnel : ITunnel
{
    private const int ErrorLineCapacity = 20;

    private readonly Process _process;
    private readonly BoundedLineBuffer _errors = new(ErrorLineCapacity);
    private readonly ILogger _logger;
    private readonly Task _stderrPump;
    private readonly Task _stdoutDrain;
    private int _disposed;

    private OpenSshTunnel(TunnelSpec spec, string via, Process process, ILogger logger)
    {
        Spec = spec;
        Via = via;
        _process = process;
        _logger = logger;
        _stderrPump = PumpErrorsAsync();
        _stdoutDrain = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        Completion = WaitForExitAsync();
    }

    public TunnelSpec Spec { get; }

    public string Via { get; }

    public Task<TunnelExit> Completion { get; }

    /// <summary>
    /// Starts <c>ssh</c> and waits up to <paramref name="startupGrace"/> for it to fail. With
    /// <c>ExitOnForwardFailure=yes</c>, SSH exits quickly when the local port cannot be bound.
    /// </summary>
    public static async Task<OpenSshTunnel> StartAsync(
        string sshExecutable,
        IReadOnlyList<string> arguments,
        TunnelSpec spec,
        string via,
        TimeSpan startupGrace,
        Processes.IChildProcessTracker tracker,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = sshExecutable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var commandLine = string.Join(' ', arguments);
        Log.SshStarting(logger, commandLine);
        var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            process.Dispose();
            throw new DockerPortMirrorException(
                $"Unable to start '{sshExecutable}'. Ensure the OpenSSH client is installed and on PATH.", ex);
        }

        tracker.Track(process);
        var tunnel = new OpenSshTunnel(spec, via, process, logger);

        try
        {
            await Task.WhenAny(tunnel.Completion, Task.Delay(startupGrace, cancellationToken)).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch
        {
            await tunnel.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        if (tunnel.Completion.IsCompleted)
        {
            var exit = await tunnel.Completion.ConfigureAwait(false);
            await tunnel.DisposeAsync().ConfigureAwait(false);
            throw new TunnelStartException(exit.Reason);
        }

        return tunnel;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (Win32Exception)
        {
            // Best effort during shutdown.
        }

        // Completion reads the exit code: let it finish before the process is disposed.
        await Task.WhenAny(Completion, Task.Delay(TimeSpan.FromSeconds(3))).ConfigureAwait(false);
        _process.Dispose();
    }

    private async Task<TunnelExit> WaitForExitAsync()
    {
        int? exitCode = null;
        try
        {
            await _process.WaitForExitAsync().ConfigureAwait(false);
            exitCode = _process.ExitCode;
        }
        catch (InvalidOperationException)
        {
            // The process object was disposed.
        }

        // Standard error closes with the process, unless a child (such as a ProxyCommand) still holds it.
        await Task.WhenAny(Task.WhenAll(_stderrPump, _stdoutDrain), Task.Delay(TimeSpan.FromSeconds(1))).ConfigureAwait(false);
        return new TunnelExit(exitCode, _errors.ToList());
    }

    private async Task PumpErrorsAsync()
    {
        try
        {
            var reader = _process.StandardError;
            while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                _errors.Add(line);
                Log.SshOutput(_logger, Spec.LocalPort, line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The process was killed and disposed while reading.
        }
    }
}
