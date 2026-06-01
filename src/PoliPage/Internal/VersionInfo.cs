using System.Reflection;

namespace PoliPage.Internal;

internal static class VersionInfo
{
    // Read from AssemblyInformationalVersion at runtime so it always matches the package.
    // Why: SourceLink/CI stamps a `+<commitHash>` suffix onto InformationalVersion. The
    // raw '+' character is technically legal in HTTP product tokens (RFC 7230) but trips
    // some strict proxies and ParseAdd's looser validation. Strip it so the User-Agent
    // header carries only the semver portion: `poli-page-sdk-dotnet/1.0.0`.
    internal static readonly string Version =
        (typeof(VersionInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
            ?? typeof(VersionInfo).Assembly.GetName().Version?.ToString()
            ?? "0.0.0")
        .Split('+')[0];
}
