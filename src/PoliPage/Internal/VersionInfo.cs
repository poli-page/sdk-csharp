using System.Reflection;

namespace PoliPage.Internal;

internal static class VersionInfo
{
    // Read from AssemblyInformationalVersion at runtime so it always matches the package.
    internal static readonly string Version =
        typeof(VersionInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? typeof(VersionInfo).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";
}
