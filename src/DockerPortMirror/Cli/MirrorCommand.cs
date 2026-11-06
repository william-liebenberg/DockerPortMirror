using System.CommandLine;
using DockerPortMirror.Diagnostics;

namespace DockerPortMirror.Cli;

/// <summary>The command-line interface: options, help, <c>--version</c> and validation.</summary>
internal static class MirrorCommand
{
    /// <summary>
    /// The Generic Host handles Ctrl+C and SIGTERM itself and shuts down gracefully, so System.CommandLine must not.
    /// </summary>
    public static InvocationConfiguration InvocationConfiguration => new() { ProcessTerminationTimeout = null };

    public static RootCommand Create(Func<MirrorCommandLine, CancellationToken, Task<int>> run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var context = new Option<string?>("--context")
        {
            Description = "Docker context to mirror. Default: the current context (docker context show).",
            HelpName = "name",
        };
        var sshTarget = new Option<string?>("--ssh-target")
        {
            Description = "SSH host alias, user@host or ssh://user@host:port. Default: derived from the context's ssh:// endpoint.",
            HelpName = "target",
        };
        var bind = new Option<string?>("--bind")
        {
            Description = $"Local bind address. Default: {MirrorOptions.DefaultBindAddress}.",
            HelpName = "address",
        };
        bind.AcceptOnlyFromAmong([.. MirrorOptions.AllowedBindAddresses]);
        var poll = new Option<int?>("--poll")
        {
            Description = $"Docker polling interval in seconds. Default: {MirrorOptions.DefaultPollSeconds}.",
            HelpName = "seconds",
        };
        poll.Validators.Add(result =>
        {
            // Read the token: GetValueOrDefault throws when the value is not a number, which the parser already reports.
            if (result.Tokens is [var token]
                && int.TryParse(token.Value, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
                && MirrorOptionsValidator.ValidatePoll(seconds) is { } error)
            {
                result.AddError(error);
            }
        });
        var verbose = new Option<bool>("--verbose", "-v")
        {
            Description = "Show SSH and Docker diagnostics, and stack traces of unexpected errors.",
        };
        var noDashboard = new Option<bool>("--no-dashboard")
        {
            Description = "Print one line per event instead of the live dashboard. Automatic when output is redirected.",
        };
        var config = new Option<FileInfo>("--config")
        {
            Description = "Configuration file (JSON). Default: config.json in the user configuration folder, when it exists.",
            HelpName = "path",
        };
        config.AcceptExistingOnly();

        var root = new RootCommand(
            $"{AppInfo.Name} mirrors the TCP ports published on a remote Docker host (reached through an SSH Docker context) " +
            "to this machine's localhost, with one OpenSSH local forward per port.")
        {
            context,
            sshTarget,
            bind,
            poll,
            verbose,
            noDashboard,
            config,
        };

        root.SetAction((parseResult, cancellationToken) => run(
            new MirrorCommandLine(
                parseResult.GetValue(context),
                parseResult.GetValue(sshTarget),
                parseResult.GetValue(bind),
                parseResult.GetValue(poll),
                parseResult.GetValue(verbose),
                parseResult.GetValue(config),
                parseResult.GetValue(noDashboard)),
            cancellationToken));

        return root;
    }
}
