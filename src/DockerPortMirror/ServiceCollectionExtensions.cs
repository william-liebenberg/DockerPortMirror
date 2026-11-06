using DockerPortMirror.Cli;
using DockerPortMirror.Docker;
using DockerPortMirror.Mirroring;
using DockerPortMirror.Processes;
using DockerPortMirror.Ssh;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DockerPortMirror;

internal static class ServiceCollectionExtensions
{
    /// <summary>Registers the whole tool. A new port source or transport replaces one registration here.</summary>
    public static IServiceCollection AddDockerPortMirror(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MirrorOptions>().Bind(configuration).ValidateOnStart();
        services.AddSingleton<IValidateOptions<MirrorOptions>, MirrorOptionsValidator>();
        services.AddSingleton(TimeProvider.System);

        services.AddProcesses();

        services.AddSingleton<DockerContextResolver>();
        services.AddSingleton<IPublishedPortSource, DockerCliPortSource>();
        services.AddSingleton<ITunnelFactory, OpenSshTunnelFactory>();

        services.AddSingleton<TunnelReconciler>();
        services.AddSingleton<MirrorWorker>();
        services.AddHostedService(provider => provider.GetRequiredService<MirrorWorker>());
        return services;
    }
}
