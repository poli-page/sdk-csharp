using System.Text.RegularExpressions;
using Xunit;

namespace PoliPage.IntegrationTests;

/// <summary>
/// A test that runs against the real API with <c>POLI_PAGE_API_KEY</c>: a
/// <c>pp_sandbox_</c> key of an organization whose <c>getting-started</c> project
/// is untouched. Without the key the test is skipped, not failed.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SandboxFactAttribute : FactAttribute
{
    public SandboxFactAttribute()
    {
        if (Sandbox.ApiKey is null)
        {
            Skip = "POLI_PAGE_API_KEY is not set";
        }
    }
}

internal static partial class Sandbox
{
    // GitHub Actions passes an unset secret as "", so an empty value counts as unset.
    public static string? ApiKey => NonEmpty("POLI_PAGE_API_KEY");

    public static Uri? BaseUrl => NonEmpty("POLI_PAGE_BASE_URL") is { } url ? new Uri(url) : null;

    public static RenderInput Welcome { get; } = new()
    {
        Project = "getting-started",
        Template = "welcome",
        Data = new { name = "SDK Integration" },
    };

    public static PoliPageClient CreateClient() => new(new PoliPageClientOptions
    {
        ApiKey = ApiKey ?? throw new InvalidOperationException("POLI_PAGE_API_KEY is not set"),
        BaseUrl = BaseUrl,
    });

    /// <summary>The environment the key names: <c>pp_{env}_…</c> (contract prefix regex).</summary>
    public static string KeyEnvironment()
    {
        var match = KeyPrefix().Match(ApiKey ?? string.Empty);
        return match.Success
            ? match.Groups["env"].Value
            : throw new InvalidOperationException("POLI_PAGE_API_KEY does not start with pp_{env}_");
    }

    private static string? NonEmpty(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;

    [GeneratedRegex("^pp_(?<env>[a-z][a-z0-9]*(?:-[a-z0-9]+)*)_", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPrefix();
}
