using System.Diagnostics;
using DockerPortMirror.Docker;
using DockerPortMirror.Ssh;

var options = Options.Parse(args);
using var appCts = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    appCts.Cancel();
};

try
{
    var docker = await DockerDiscovery.DiscoverAsync(options.Context, options.SshTarget, appCts.Token);

    Console.WriteLine("DockerPortMirror v1");
    Console.WriteLine($"Docker context : {docker.Context}");
    Console.WriteLine($"Docker endpoint: {docker.Endpoint}");
    Console.WriteLine($"SSH target    : {docker.SshTarget}");
    Console.WriteLine($"Local bind    : {options.BindAddress}");
    Console.WriteLine($"Poll interval : {options.PollSeconds}s");
    Console.WriteLine("Transport     : OpenSSH local forwarding");
    Console.WriteLine();
    Console.WriteLine("Press Ctrl+C to stop.");
    Console.WriteLine();

    using var manager = new TunnelManager(docker.SshTarget, options.BindAddress, options.Verbose);

    while (!appCts.IsCancellationRequested)
    {
        try
        {
            var ports = await DockerPorts.GetPublishedTcpPortsAsync(docker.Context, appCts.Token);
            await manager.SynchroniseAsync(ports, appCts.Token);
        }
        catch (OperationCanceledException) when (appCts.IsCancellationRequested)
        {
            break;
        }
        catch (Exception ex)
        {
            WriteWarning(ex.Message);
        }

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), appCts.Token);
        }
        catch (OperationCanceledException)
        {
            break;
        }
    }

    Console.WriteLine();
    Console.WriteLine("Stopping SSH tunnels...");
    return 0;
}
catch (OperationCanceledException)
{
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

static void WriteWarning(string message)
{
    var original = Console.ForegroundColor;
    Console.ForegroundColor = ConsoleColor.Yellow;
    Console.Error.WriteLine($"WARNING: {message}");
    Console.ForegroundColor = original;
}

internal sealed record Options(
    string? Context,
    string? SshTarget,
    string BindAddress,
    int PollSeconds,
    bool Verbose)
{
    public static Options Parse(string[] args)
    {
        string? context = null;
        string? sshTarget = null;
        var bindAddress = "127.0.0.1";
        var pollSeconds = 1;
        var verbose = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--context":
                    context = RequireValue(args, ref i, "--context");
                    break;
                case "--ssh-target":
                    sshTarget = RequireValue(args, ref i, "--ssh-target");
                    break;
                case "--bind":
                    bindAddress = RequireValue(args, ref i, "--bind");
                    break;
                case "--poll":
                    if (!int.TryParse(RequireValue(args, ref i, "--poll"), out pollSeconds) || pollSeconds < 1)
                        throw new ArgumentException("--poll must be an integer of at least 1 second.");
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--help":
                case "-h":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {args[i]}. Use --help for usage.");
            }
        }

        if (bindAddress is not "127.0.0.1" and not "localhost")
            throw new ArgumentException("For safety, --bind must be 127.0.0.1 or localhost.");

        return new(context, sshTarget, bindAddress, pollSeconds, verbose);
    }

    private static string RequireValue(string[] args, ref int index, string option)
    {
        if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]))
            throw new ArgumentException($"{option} requires a value.");
        return args[index];
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            DockerPortMirror v1

            Mirrors TCP ports published by a remote SSH Docker context to Windows localhost
            by creating one OpenSSH local-forwarding process per published port.

            Usage:
              DockerPortMirror.exe [options]

            Options:
              --context <name>       Docker context. Defaults to `docker context show`.
              --ssh-target <target>  SSH target/alias. Defaults to the context's ssh:// endpoint.
              --bind <address>       Local bind address: 127.0.0.1 or localhost. Default: 127.0.0.1.
              --poll <seconds>       Docker polling interval. Default: 2.
              --verbose              Show SSH diagnostic messages.
              --help                 Show this help.
            """);
    }
}

internal sealed record DockerConfiguration(string Context, string Endpoint, string SshTarget);

internal static class DockerDiscovery
{
    public static async Task<DockerConfiguration> DiscoverAsync(
        string? contextOverride,
        string? sshTargetOverride,
        CancellationToken token)
    {
        var context = contextOverride;
        if (string.IsNullOrWhiteSpace(context))
        {
            var current = await ProcessRunner.RunAsync("docker", ["context", "show"], token);
            current.EnsureSuccess("Unable to determine the current Docker context");
            context = current.StandardOutput.Trim();
        }

        if (string.IsNullOrWhiteSpace(context))
            throw new InvalidOperationException("Docker did not return a current context.");

        var inspect = await ProcessRunner.RunAsync("docker", ["context", "inspect", context], token);
        inspect.EnsureSuccess($"Unable to inspect Docker context '{context}'");

        var endpoint = DockerContextResolver.ParseEndpoint(inspect.StandardOutput)
            ?? throw new InvalidOperationException($"Docker context '{context}' has no Docker endpoint.");

        var sshTarget = sshTargetOverride;
        if (string.IsNullOrWhiteSpace(sshTarget))
            sshTarget = SshTarget.FromEndpoint(endpoint);

        if (string.IsNullOrWhiteSpace(sshTarget))
        {
            throw new InvalidOperationException(
                $"The selected Docker endpoint is not SSH-based: '{endpoint}'. " +
                "Select an ssh:// Docker context or pass --ssh-target <alias-or-user@host>.");
        }

        var check = await ProcessRunner.RunAsync(
            "ssh",
            ["-G", sshTarget],
            token);
        check.EnsureSuccess($"OpenSSH could not resolve SSH target '{sshTarget}'");

        return new(context, endpoint, sshTarget);
    }
}

internal static class DockerPorts
{
    public static async Task<HashSet<int>> GetPublishedTcpPortsAsync(string context, CancellationToken token)
    {
        var result = await ProcessRunner.RunAsync(
            "docker",
            ["--context", context, "ps", "--format", "{{.Ports}}"],
            token);
        result.EnsureSuccess($"Unable to query Docker context '{context}'");

        return DockerPortsParser.Parse(result.StandardOutput);
    }
}

internal sealed class TunnelManager : IDisposable
{
    private readonly string _sshTarget;
    private readonly string _bindAddress;
    private readonly bool _verbose;
    private readonly Dictionary<int, SshTunnel> _tunnels = new();

    public TunnelManager(string sshTarget, string bindAddress, bool verbose)
    {
        _sshTarget = sshTarget;
        _bindAddress = bindAddress;
        _verbose = verbose;
    }

    public async Task SynchroniseAsync(HashSet<int> requiredPorts, CancellationToken token)
    {
        foreach (var dead in _tunnels.Where(x => x.Value.HasExited).Select(x => x.Key).ToArray())
        {
            var tunnel = _tunnels[dead];
            var error = await tunnel.GetErrorAsync();
            tunnel.Dispose();
            _tunnels.Remove(dead);
            WriteLine(ConsoleColor.Red, $" ! SSH tunnel for localhost:{dead} exited{FormatError(error)}");
        }

        foreach (var port in _tunnels.Keys.Except(requiredPorts).ToArray())
        {
            WriteLine(ConsoleColor.Yellow, $" - Removing localhost:{port}");
            _tunnels[port].Dispose();
            _tunnels.Remove(port);
        }

        foreach (var port in requiredPorts.Except(_tunnels.Keys).Order().ToArray())
        {
            token.ThrowIfCancellationRequested();
            var tunnel = await SshTunnel.StartAsync(_sshTarget, _bindAddress, port, _verbose, token);
            _tunnels.Add(port, tunnel);
            WriteLine(ConsoleColor.Green, $" + localhost:{port} -> SSH {_sshTarget} -> Mac localhost:{port}");
        }
    }

    public void Dispose()
    {
        foreach (var tunnel in _tunnels.Values)
            tunnel.Dispose();
        _tunnels.Clear();
    }

    private static string FormatError(string error) =>
        string.IsNullOrWhiteSpace(error) ? "." : $": {error.Trim()}";

    private static void WriteLine(ConsoleColor color, string text)
    {
        var original = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine(text);
        Console.ForegroundColor = original;
    }
}

internal sealed class SshTunnel : IDisposable
{
    private readonly Process _process;
    private readonly Task<string> _stderr;
    private bool _disposed;

    private SshTunnel(Process process, Task<string> stderr)
    {
        _process = process;
        _stderr = stderr;
    }

    public bool HasExited => _process.HasExited;

    public static async Task<SshTunnel> StartAsync(
        string sshTarget,
        string bindAddress,
        int port,
        bool verbose,
        CancellationToken token)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "ssh",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        var arguments = new List<string>
        {
            "-N",
            "-T",
            "-o", "ExitOnForwardFailure=yes",
            "-o", "BatchMode=yes",
            "-o", "ServerAliveInterval=30",
            "-o", "ServerAliveCountMax=3",
            "-o", "ConnectTimeout=10",
            "-L", $"{bindAddress}:{port}:127.0.0.1:{port}"
        };

        if (verbose)
            arguments.Add("-v");

        arguments.Add(sshTarget);
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            process.Dispose();
            throw new InvalidOperationException("Unable to start ssh.exe. Ensure the Windows OpenSSH Client is installed and on PATH.", ex);
        }

        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        _ = process.StandardOutput.ReadToEndAsync(CancellationToken.None);

        // ExitOnForwardFailure makes SSH terminate quickly when the local port cannot bind.
        await Task.Delay(300, token);
        if (process.HasExited)
        {
            var error = await stderr;
            process.Dispose();
            throw new InvalidOperationException(
                $"Could not mirror localhost:{port}. SSH exited with code {process.ExitCode}: {error.Trim()}");
        }

        return new(process, stderr);
    }

    public async Task<string> GetErrorAsync()
    {
        if (!_process.HasExited)
            return string.Empty;
        return await _stderr;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(3000);
            }
        }
        catch
        {
            // Best-effort cleanup during shutdown.
        }
        finally
        {
            _process.Dispose();
        }
    }
}

internal sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public void EnsureSuccess(string message)
    {
        if (ExitCode == 0)
            return;
        var detail = string.IsNullOrWhiteSpace(StandardError) ? StandardOutput : StandardError;
        throw new InvalidOperationException($"{message}. {detail.Trim()}");
    }
}

internal static class ProcessRunner
{
    public static async Task<ProcessResult> RunAsync(
        string executable,
        IEnumerable<string> arguments,
        CancellationToken token)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Unable to start '{executable}'. Ensure it is installed and available on PATH.", ex);
        }

        var stdout = process.StandardOutput.ReadToEndAsync(token);
        var stderr = process.StandardError.ReadToEndAsync(token);
        await process.WaitForExitAsync(token);
        return new(process.ExitCode, await stdout, await stderr);
    }
}
