using Microsoft.Extensions.Logging;

namespace PoliPage;

/// <summary>
/// Configuration options for <see cref="PoliPageClient"/>.
/// </summary>
/// <remarks>
/// Properties use mutable setters because the .NET <c>IOptions</c> pattern
/// (via <see cref="ServiceCollectionExtensions.AddPoliPage"/>) populates
/// options through an <c>Action&lt;T&gt;</c> callback that requires write
/// access. The record is still safe to share once constructed — the SDK
/// snapshots the values it needs at <see cref="PoliPageClient"/> construction.
/// </remarks>
public sealed record PoliPageClientOptions
{
    /// <summary>
    /// The Poli Page API key used to authenticate requests.
    /// Must start with <c>pp_</c>. Required — the client will throw
    /// <see cref="System.ArgumentException"/> at construction time when this is
    /// <see langword="null"/>, empty, or whitespace.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Override for the API base URL.
    /// When <see langword="null"/> (the default), the client uses
    /// <c>https://api.poli.page</c>.
    /// </summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>
    /// Maximum number of automatic retries for transient failures (5xx and 429).
    /// Must be ≥ 0. Defaults to <c>2</c>.
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// Base delay applied between retry attempts before jitter.
    /// Must be greater than <see cref="TimeSpan.Zero"/>. Defaults to 500 ms.
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Per-request timeout applied to every HTTP call.
    /// Must be greater than <see cref="TimeSpan.Zero"/>. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// An externally-managed <see cref="System.Net.Http.HttpClient"/> to use for
    /// all API requests. When provided, the client takes no ownership of it and
    /// will not dispose it. When <see langword="null"/> (the default), the client
    /// creates and owns its own instance.
    /// </summary>
    public System.Net.Http.HttpClient? HttpClient { get; set; }

    /// <summary>
    /// An externally-managed <see cref="System.Net.Http.HttpClient"/> dedicated to
    /// large binary downloads (e.g. PDF streams). Wired up in Phase 5.
    /// When <see langword="null"/>, the same <see cref="HttpClient"/> is used.
    /// </summary>
    public System.Net.Http.HttpClient? DownloadHttpClient { get; set; }

    /// <summary>
    /// Optional logger for SDK diagnostic output.
    /// When <see langword="null"/>, a no-op logger is used.
    /// </summary>
    public ILogger<PoliPageClient>? Logger { get; set; }

    /// <summary>
    /// Callback invoked before each retry attempt.
    /// Use this to log or instrument retry behaviour.
    /// </summary>
    public Action<RetryEvent>? OnRetry { get; set; }

    /// <summary>
    /// Callback invoked when a request fails after all retries are exhausted.
    /// Receives the final exception that caused the failure.
    /// Hook exceptions are swallowed — the original failure still surfaces to the caller.
    /// </summary>
    public Action<Exception>? OnError { get; set; }

    /// <summary>
    /// Callback invoked once per HTTP attempt, immediately before the request is sent.
    /// Fires every time, including each retry attempt. Reference: <c>sdk-node/src/index.ts:186-190</c>.
    /// </summary>
    public Action<RequestEvent>? OnRequest { get; set; }

    /// <summary>
    /// Callback invoked once per successful (2xx) HTTP response. Non-2xx responses do
    /// not fire this hook — they surface through <see cref="OnRetry"/> (transient) or
    /// <see cref="OnError"/> (terminal). Reference: <c>sdk-node/src/index.ts:224-228</c>.
    /// </summary>
    public Action<ResponseEvent>? OnResponse { get; set; }
}
