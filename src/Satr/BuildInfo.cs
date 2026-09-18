using System.Reflection;
using System.Runtime.InteropServices;

namespace Satr;

/// <summary>Lightweight build identity shown in Settings and diagnostics.</summary>
internal static class BuildInfo
{
    public static string Version { get; } = ReadVersion();
    public static string Commit { get; } = ReadMetadata("SatrCommit", "unknown");
    public static string Channel { get; } = ReadMetadata("SatrChannel", "Development");
    public static bool IsDevelopment => Channel != "Release";
    public static string Runtime { get; } =
        $"{RuntimeInformation.FrameworkDescription} · {RuntimeInformation.OSDescription.Trim()} ({RuntimeInformation.OSArchitecture})";
    public static string Summary => $"Satr {Version} ({Channel}, {Commit}) · {Runtime}";

    private static string ReadVersion()
    {
        var informational = typeof(BuildInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var version = (informational ?? typeof(BuildInfo).Assembly.GetName().Version?.ToString() ?? "0.0.0").Split('+')[0];
        return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version;
    }

    private static string ReadMetadata(string key, string fallback)
    {
        var value = typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == key)?.Value;
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
