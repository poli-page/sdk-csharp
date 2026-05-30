using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PoliPage;

/// <summary>
/// Extensions for registering Poli Page services with <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="PoliPageClient"/> as a singleton, wired through
    /// <see cref="IHttpClientFactory"/> for socket lifetime management. Two named
    /// HttpClients are created: <c>"PoliPage"</c> for API requests, and
    /// <c>"PoliPage.Download"</c> for presigned-URL fetches (the download client
    /// is header-less so S3 cannot reject the request on an unexpected
    /// Authorization or User-Agent).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Validation runs at host startup via <c>ValidateOnStart</c> so a misconfigured
    /// host fails fast rather than on the first SDK call in production.
    /// </para>
    /// <para>
    /// Retry, idempotency, error mapping, and User-Agent headers all live inside
    /// <see cref="PoliPageClient"/> itself — this method does NOT register any
    /// <c>DelegatingHandler</c>. That keeps the DI path behaviorally identical to
    /// the non-DI constructor, and lets callers wire Polly or custom handlers on
    /// the named HttpClient without colliding with SDK internals.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Callback that populates the <see cref="PoliPageClientOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddPoliPage(
        this IServiceCollection services,
        Action<PoliPageClientOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddOptions<PoliPageClientOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey),
                "PoliPage: ApiKey is required.")
            .Validate(o => o.MaxRetries >= 0,
                "PoliPage: MaxRetries must be ≥ 0.")
            .Validate(o => o.RetryDelay > TimeSpan.Zero,
                "PoliPage: RetryDelay must be > 0.")
            .Validate(o => o.RequestTimeout > TimeSpan.Zero,
                "PoliPage: RequestTimeout must be > 0.")
            .ValidateOnStart();

        services.AddHttpClient("PoliPage");
        services.AddHttpClient("PoliPage.Download");

        services.AddSingleton<PoliPageClient>(static sp =>
        {
            var opts = sp.GetRequiredService<IOptions<PoliPageClientOptions>>().Value;
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var logger = sp.GetService<ILogger<PoliPageClient>>();

            return new PoliPageClient(opts with
            {
                HttpClient = factory.CreateClient("PoliPage"),
                DownloadHttpClient = factory.CreateClient("PoliPage.Download"),
                Logger = opts.Logger ?? logger,
            });
        });

        return services;
    }
}
