using Xunit;

namespace PoliPage.IntegrationTests;

/// <summary>
/// A test that runs against the real API with <c>POLI_PAGE_API_KEY</c>: a
/// <c>pp_sandbox_</c> key of an organization whose <c>getting-started</c> project
/// is untouched. Without the key, or with a key of another environment, the
/// test is skipped, not failed: it never renders into a counted environment.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class SandboxFactAttribute : FactAttribute
{
    public SandboxFactAttribute()
    {
        Skip = Sandbox.SkipReason(Sandbox.ApiKey);
    }
}

internal static class Sandbox
{
    private const string SandboxKeyPrefix = "pp_sandbox_";

    // GitHub Actions passes an unset secret as "", so an empty value counts as unset.
    public static string? ApiKey => NonEmpty("POLI_PAGE_API_KEY");

    /// <summary>Why a sandbox test does not run with <paramref name="apiKey"/>, or <see langword="null"/> when it runs.</summary>
    public static string? SkipReason(string? apiKey) => apiKey switch
    {
        null => "POLI_PAGE_API_KEY is not set",
        _ when !apiKey.StartsWith(SandboxKeyPrefix, StringComparison.Ordinal) =>
            "POLI_PAGE_API_KEY is not a pp_sandbox_ key: the integration tests render in sandbox only",
        _ => null,
    };

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

    private static string? NonEmpty(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
}
