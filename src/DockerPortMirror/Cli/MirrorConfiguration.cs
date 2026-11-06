using Microsoft.Extensions.Configuration;

namespace DockerPortMirror.Cli;

/// <summary>Where the options come from, in increasing order of precedence.</summary>
internal static class MirrorConfiguration
{
    public const string EnvironmentPrefix = "DOCKERPORTMIRROR_";

    /// <summary>The default configuration file: <c>%APPDATA%\DockerPortMirror\config.json</c> or <c>~/.config/DockerPortMirror/config.json</c>.</summary>
    public static string DefaultConfigFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DockerPortMirror",
        "config.json");

    public static IConfigurationBuilder AddMirrorConfiguration(this IConfigurationBuilder configuration, MirrorCommandLine commandLine)
    {
        ArgumentNullException.ThrowIfNull(commandLine);
        if (commandLine.ConfigFile is { } file)
            configuration.AddJsonFile(file.FullName, optional: false, reloadOnChange: false);
        else
            configuration.AddJsonFile(DefaultConfigFile, optional: true, reloadOnChange: false);

        configuration.AddEnvironmentVariables(EnvironmentPrefix);
        configuration.AddInMemoryCollection(commandLine.ToConfiguration());
        return configuration;
    }
}
