using System.Reflection;

namespace DockerPortMirror.Diagnostics;

/// <summary>Name and version of the tool, read from the assembly (the version comes from the git tag through MinVer).</summary>
internal static class AppInfo
{
    public const string Name = "DockerPortMirror";

    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var version = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(AppInfo).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata >= 0 ? version[..metadata] : version;
    }
}
