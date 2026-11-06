using System.Diagnostics;
using DockerPortMirror;
using DockerPortMirror.Docker;
using DockerPortMirror.Mirroring;
using DockerPortMirror.Processes;
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
        var pollSeconds = 2;
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

internal sealed record DockerConfiguration(string Context, string Endpoint, SshTarget SshTarget);

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
            var current = await Commands.RunAsync("docker", ["context", "show"], token);
            Commands.EnsureSuccess(current, "Unable to determine the current Docker context");
            context = current.StandardOutput.Trim();
        }

        if (string.IsNullOrWhiteSpace(context))
            throw new InvalidOperationException("Docker did not return a current context.");

        var inspect = await Commands.RunAsync("docker", ["context", "inspect", context], token);
        Commands.EnsureSuccess(inspect, $"Unable to inspect Docker context '{context}'");

        var endpoint = DockerContextResolver.ParseEndpoint(inspect.StandardOutput)
            ?? throw new InvalidOperationException($"Docker context '{context}' has no Docker endpoint.");

        var sshTarget = string.IsNullOrWhiteSpace(sshTargetOverride)
            ? SshTarget.FromEndpoint(endpoint)
            : new SshTarget(sshTargetOverride);

        if (sshTarget is null)
        {
            throw new InvalidOperationException(
                $"The selected Docker endpoint is not SSH-based: '{endpoint}'. " +
                "Select an ssh:// Docker context or pass --ssh-target <alias-or-user@host>.");
        }

        var check = await Commands.RunAsync(
            "ssh",
            ["-G", sshTarget.Value.Destination],
            token);
        Commands.EnsureSuccess(check, $"OpenSSH could not resolve SSH target '{sshTarget}'");

        return new(context, endpoint, sshTarget.Value);
    }
}

internal static class DockerPorts
{
    public static async Task<IReadOnlySet<PublishedPort>> GetPublishedTcpPortsAsync(string context, CancellationToken token)
    {
        var result = await Commands.RunAsync(
            "docker",
            ["--context", context, "ps", "--format", "{{.Ports}}"],
            token);
        Commands.EnsureSuccess(result, $"Unable to query Docker context '{context}'");

        return DockerPortsParser.Parse(result.StandardOutput);
    }
}

internal sealed class TunnelManager : IDisposable
{
    private readonly SshTarget _sshTarget;
    private readonly string _bindAddress;
    private readonly bool _verbose;
    private readonly Dictionary<TunnelSpec, SshTunnel> _tunnels = new();

    public TunnelManager(SshTarget sshTarget, string bindAddress, bool verbose)
    {
        _sshTarget = sshTarget;
        _bindAddress = bindAddress;
        _verbose = verbose;
    }

    public async Task SynchroniseAsync(IReadOnlySet<PublishedPort> publishedPorts, CancellationToken token)
    {
        var required = TunnelSpec.FromPublishedPorts(publishedPorts, _bindAddress);

        foreach (var dead in _tunnels.Where(x => x.Value.HasExited).Select(x => x.Key).ToArray())
        {
            var tunnel = _tunnels[dead];
            var error = await tunnel.GetErrorAsync();
            tunnel.Dispose();
            _tunnels.Remove(dead);
            WriteLine(ConsoleColor.Red, $" ! SSH tunnel for localhost:{dead.LocalPort} exited{FormatError(error)}");
        }

        foreach (var spec in _tunnels.Keys.Except(required).ToArray())
        {
            WriteLine(ConsoleColor.Yellow, $" - Removing localhost:{spec.LocalPort}");
            _tunnels[spec].Dispose();
            _tunnels.Remove(spec);
        }

        foreach (var spec in required.Except(_tunnels.Keys).OrderBy(x => x.LocalPort).ToArray())
        {
            token.ThrowIfCancellationRequested();
            var tunnel = await SshTunnel.StartAsync(_sshTarget, spec, _verbose, token);
            _tunnels.Add(spec, tunnel);
            WriteLine(ConsoleColor.Green, $" + localhost:{spec.LocalPort} -> {_sshTarget} -> {spec.RemoteHost}:{spec.RemotePort}");
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
        SshTarget sshTarget,
        TunnelSpec spec,
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
            "-L", $"{spec.BindAddress}:{spec.LocalPort}:{spec.RemoteHost}:{spec.RemotePort}"
        };

        if (verbose)
            arguments.Add("-v");

        arguments.Add(sshTarget.Destination);
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
            throw new DockerPortMirrorException("Unable to start ssh. Ensure the OpenSSH client is installed and on PATH.", ex);
        }

        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        _ = process.StandardOutput.ReadToEndAsync(CancellationToken.None);

        // ExitOnForwardFailure makes SSH terminate quickly when the local port cannot bind.
        await Task.Delay(300, token);
        if (process.HasExited)
        {
            var error = await stderr;
            var exitCode = process.ExitCode;
            process.Dispose();
            throw new DockerPortMirrorException(
                $"Could not mirror localhost:{spec.LocalPort}. SSH exited with code {exitCode}: {error.Trim()}");
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

internal static class Commands
{
    private static readonly ProcessRunner Runner = new ProcessRunner();
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments, CancellationToken token) =>
        Runner.RunAsync(fileName, arguments, Timeout, token);

    public static void EnsureSuccess(ProcessResult result, string message)
    {
        if (!result.Succeeded)
            throw new DockerPortMirrorException($"{message}. {result.ErrorDetail}");
    }
}
