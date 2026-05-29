using Microsoft.Extensions.Logging;

namespace PoliPage;

/// <summary>
/// Configuration options for <see cref="PoliPageClient"/>.
/// All properties use <see langword="init"/>-only setters so the record can be
/// created with object-initializer syntax and safely shared across requests.
/// </summary>
public sealed record PoliPageClientOptions
{
    /// <summary>
    /// The Poli Page API key used to authenticate requests.
    /// Must start with <c>pp_</c>. Required — the client will throw
    /// <see cref="System.ArgumentException"/> at construction time when this is
    /// <see langword="null"/>, empty, or whitespace.
    /// </summary>
    public string ApiKey { get; init; } = string.Empty;

    /// <summary>
    /// Override for the API base URL.
    /// When <see langword="null"/> (the default), the client uses
    /// <c>https://api.poli.page</c>.
    /// </summary>
    public Uri? BaseUrl { get; init; }

    /// <summary>
    /// Maximum number of automatic retries for transient failures (5xx and 429).
    /// Must be ≥ 0. Defaults to <c>2</c>.
    /// </summary>
    public int MaxRetries { get; init; } = 2;

    /// <summary>
    /// Base delay applied between retry attempts before jitter.
    /// Must be greater than <see cref="TimeSpan.Zero"/>. Defaults to 500 ms.
    /// </summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Per-request timeout applied to every HTTP call.
    /// Must be greater than <see cref="TimeSpan.Zero"/>. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// An externally-managed <see cref="System.Net.Http.HttpClient"/> to use for
    /// all API requests. When provided, the client takes no ownership of it and
    /// will not dispose it. When <see langword="null"/> (the default), the client
    /// creates and owns its own instance.
    /// </summary>
    public System.Net.Http.HttpClient? HttpClient { get; init; }

    /// <summary>
    /// An externally-managed <see cref="System.Net.Http.HttpClient"/> dedicated to
    /// large binary downloads (e.g. PDF streams). Wired up in Phase 5.
    /// When <see langword="null"/>, the same <see cref="HttpClient"/> is used.
    /// </summary>
    public System.Net.Http.HttpClient? DownloadHttpClient { get; init; }

    /// <summary>
    /// Optional logger for SDK diagnostic output.
    /// When <see langword="null"/>, a no-op logger is used.
    /// </summary>
    public ILogger<PoliPageClient>? Logger { get; init; }

    /// <summary>
    /// Callback invoked before each retry attempt.
    /// Use this to log or instrument retry behaviour.
    /// </summary>
    public Action<RetryEvent>? OnRetry { get; init; }

    /// <summary>
    /// Callback invoked when a request fails after all retries are exhausted.
    /// Receives the final exception that caused the failure.
    /// </summary>
    public Action<Exception>? OnError { get; init; }
}
