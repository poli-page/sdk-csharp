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
    /// Must start with <c>pp_</c>. Required — the constructor throws a
    /// <see cref="PoliPageException"/> with code <c>invalid_options</c> (message
    /// <c>apiKey is required</c>) when this is <see langword="null"/>, empty, or whitespace.
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
    /// Timeout of each API attempt: the connection, the answer's headers and its whole body
    /// together. Each retry gets its own. It applies whatever <see cref="HttpClient"/> you
    /// inject (should that client's own <see cref="System.Net.Http.HttpClient.Timeout"/> be
    /// shorter, that one fires first and the timeout error names it). It does not apply to the
    /// presigned PDF download (see <see cref="DownloadHttpClient"/>).
    /// Must be greater than <see cref="TimeSpan.Zero"/>. Defaults to 60 seconds.
    /// </summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// An externally-managed <see cref="System.Net.Http.HttpClient"/> to use for
    /// all API requests. When provided, the client takes no ownership of it and
    /// will not dispose it. When <see langword="null"/> (the default), the client
    /// creates and owns its own instance. If its <c>DefaultRequestHeaders.ExpectContinue</c> is
    /// <see langword="true"/>, the HTTP handler may resend a request whose body has not been
    /// sent yet, outside the SDK's counted retries; leave it unset.
    /// </summary>
    public System.Net.Http.HttpClient? HttpClient { get; set; }

    /// <summary>
    /// An externally-managed <see cref="System.Net.Http.HttpClient"/> for the presigned PDF
    /// downloads (<see cref="Render.PdfAsync"/>, <see cref="Render.PdfStreamAsync"/>,
    /// <see cref="DocumentDescriptor.DownloadPdfAsync"/>). Its own
    /// <see cref="System.Net.Http.HttpClient.Timeout"/> bounds only the wait for the storage's
    /// response headers (it then throws <c>DOWNLOAD_FAILED</c>); once the headers have arrived,
    /// reading the PDF body has no SDK time bound, and only the call's
    /// <see cref="CancellationToken"/> stops it. When <see langword="null"/>, the client creates
    /// and owns a bare instance (100 s timeout), separate from <see cref="HttpClient"/>, so no
    /// API header reaches the storage.
    /// </summary>
    public System.Net.Http.HttpClient? DownloadHttpClient { get; set; }

    /// <summary>
    /// Optional logger for SDK diagnostic output.
    /// When <see langword="null"/>, a no-op logger is used.
    /// </summary>
    public ILogger<PoliPageClient>? Logger { get; set; }

    /// <summary>
    /// Callback invoked before each retry's wait, with the number of the attempt about to
    /// start (<c>2</c> for the first retry), the delay, and the reason.
    /// Use this to log or instrument retry behaviour.
    /// </summary>
    public Action<RetryEvent>? OnRetry { get; set; }

    /// <summary>
    /// Callback invoked once with the error an API call is about to throw: after the
    /// retries, at once for an error that is not retried, and with <c>aborted</c> when the
    /// caller cancelled.
    /// Hook exceptions are swallowed — the original failure still surfaces to the caller.
    /// </summary>
    public Action<Exception>? OnError { get; set; }

    /// <summary>
    /// Callback invoked once per HTTP attempt, immediately before the request is sent.
    /// Fires every time, including each retry attempt. Reference: <c>sdk-node/src/index.ts:186-190</c>.
    /// </summary>
    public Action<RequestEvent>? OnRequest { get; set; }

    /// <summary>
    /// Callback invoked once per successful (2xx) HTTP response, once its whole body has been
    /// read; <c>DurationMs</c> covers the body. Non-2xx responses do
    /// not fire this hook — they surface through <see cref="OnRetry"/> (transient) or
    /// <see cref="OnError"/> (terminal). Reference: <c>sdk-node/src/index.ts:224-228</c>.
    /// </summary>
    public Action<ResponseEvent>? OnResponse { get; set; }
}
