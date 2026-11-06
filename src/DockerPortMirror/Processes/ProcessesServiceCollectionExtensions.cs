using System.ComponentModel;
using DockerPortMirror.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DockerPortMirror.Processes;

internal static class ProcessesServiceCollectionExtensions
{
    public static IServiceCollection AddProcesses(this IServiceCollection services)
    {
        services.AddSingleton<IChildProcessTracker>(provider => CreateTracker(provider.GetRequiredService<ILogger<IChildProcessTracker>>()));
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        return services;
    }

    private static IChildProcessTracker CreateTracker(ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
            return new NoOpChildProcessTracker();

        try
        {
            return new WindowsJobObjectTracker();
        }
        catch (Win32Exception ex)
        {
            Log.ChildProcessTrackingUnavailable(logger, ex.Message);
            return new NoOpChildProcessTracker();
        }
    }
}
