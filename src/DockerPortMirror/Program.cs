using DockerPortMirror;
using DockerPortMirror.Cli;
using DockerPortMirror.Dashboard;
using DockerPortMirror.Diagnostics;
using DockerPortMirror.Mirroring;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Spectre.Console;

// Composition root: parse the command line, then run the mirror in the Generic Host.
return await MirrorCommand.Create(RunAsync).Parse(args).InvokeAsync(MirrorCommand.InvocationConfiguration);

static async Task<int> RunAsync(MirrorCommandLine commandLine, CancellationToken cancellationToken)
{
    try
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.Configuration.AddMirrorConfiguration(commandLine);
        var verbose = builder.Configuration.GetValue<bool>(nameof(MirrorOptions.Verbose));
        var useDashboard = !builder.Configuration.GetValue<bool>(nameof(MirrorOptions.NoDashboard))
            && !Console.IsOutputRedirected
            && AnsiConsole.Profile.Capabilities.Interactive;
        if (useDashboard)
        {
            // Registered before the mirror, so the host stops it last and its final frame shows the shutdown.
            var dashboardLog = new DashboardLog(TimeProvider.System);
            builder.Logging.AddMirrorDashboard(dashboardLog, verbose);
            builder.Services.AddMirrorDashboard(dashboardLog);
        }
        else
        {
            builder.Logging.AddMirrorConsole(verbose);
        }

        builder.Services.AddDockerPortMirror(builder.Configuration);
        builder.Services.Configure<ConsoleLifetimeOptions>(options => options.SuppressStatusMessages = true);
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(10));

        using var host = builder.Build();
        var worker = host.Services.GetRequiredService<MirrorWorker>();
        await host.RunAsync(cancellationToken);
        return worker.ExitCode;
    }
    catch (OptionsValidationException ex)
    {
        await Console.Error.WriteLineAsync($"ERROR: {string.Join(" ", ex.Failures)}");
        return 1;
    }
    catch (Exception ex) when (ex is InvalidDataException or FormatException
        || ex is InvalidOperationException { Source: "Microsoft.Extensions.Configuration.Binder" })
    {
        // A malformed configuration file or a value of the wrong type in configuration.
        await Console.Error.WriteLineAsync($"ERROR: Invalid configuration. {ex.Message}");
        return 1;
    }
}
