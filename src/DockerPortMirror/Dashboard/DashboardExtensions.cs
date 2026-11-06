using DockerPortMirror.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DockerPortMirror.Dashboard;

internal static class DashboardExtensions
{
    /// <summary>Logs to the dashboard's log panel instead of the console; <paramref name="verbose"/> adds diagnostics and stack traces.</summary>
    public static ILoggingBuilder AddMirrorDashboard(this ILoggingBuilder logging, DashboardLog log, bool verbose)
    {
        logging.ClearProviders();
        logging.AddMirrorFilters(verbose);
        logging.AddProvider(new DashboardLoggerProvider(log, verbose));
        return logging;
    }

    /// <summary>
    /// Registers the live dashboard. Register it before the mirror: the host stops services in reverse order, so
    /// the tunnels close and log their last lines before the dashboard paints its final frame.
    /// </summary>
    public static IServiceCollection AddMirrorDashboard(this IServiceCollection services, DashboardLog log)
    {
        services.AddSingleton(log);
        services.AddHostedService<DashboardService>();
        return services;
    }
}
