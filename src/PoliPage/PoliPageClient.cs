using System.Net.Http;
using PoliPage.Internal;

namespace PoliPage;

/// <summary>
/// The primary entry point for the Poli Page API.
/// Provides methods to render templates, stream output, and manage documents.
/// </summary>
/// <remarks>
/// <para>
/// Create a single instance per application lifetime and reuse it; the client
/// is thread-safe. Dispose the instance when the application shuts down to
/// release underlying HTTP connections.
/// </para>
/// <para>
/// When an <see cref="HttpClient"/> is supplied via
/// <see cref="PoliPageClientOptions.HttpClient"/>, the caller retains ownership
/// and must dispose it independently.
/// </para>
/// <para>
/// All API requests are issued with absolute URIs constructed from
/// <see cref="PoliPageClientOptions.BaseUrl"/> (or the default
/// <c>https://api.poli.page</c>). The
/// <see cref="System.Net.Http.HttpClient.BaseAddress"/> of a caller-provided
/// <see cref="HttpClient"/> is intentionally ignored, so the same client
/// instance can safely be shared with non-Poli-Page traffic.
/// </para>
/// </remarks>
public sealed class PoliPageClient : IDisposable
{
    /// <summary>
    /// The default base address used when
    /// <see cref="PoliPageClientOptions.BaseUrl"/> is <see langword="null"/>.
    /// </summary>
    internal static readonly Uri DefaultBaseAddress = new("https://api.poli.page");

    private readonly PoliPageClientOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;
    private readonly Render _render;
    private int _disposed;

    /// <summary>
    /// Initializes a new <see cref="PoliPageClient"/> with the supplied options.
    /// </summary>
    /// <param name="options">Client configuration. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><see cref="PoliPageClientOptions.ApiKey"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="PoliPageClientOptions.MaxRetries"/> is negative, or
    /// <see cref="PoliPageClientOptions.RetryDelay"/> or
    /// <see cref="PoliPageClientOptions.RequestTimeout"/> are not positive.
    /// </exception>
    public PoliPageClient(PoliPageClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("PoliPage: ApiKey is required.", nameof(options));

        if (options.MaxRetries < 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.MaxRetries, "PoliPage: MaxRetries must be ≥ 0.");

        if (options.RetryDelay <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.RetryDelay, "PoliPage: RetryDelay must be > 0.");

        if (options.RequestTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), options.RequestTimeout, "PoliPage: RequestTimeout must be > 0.");

        _options = options;
        BaseAddress = options.BaseUrl ?? DefaultBaseAddress;

        if (options.HttpClient is not null)
        {
            _httpClient = options.HttpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _httpClient = new HttpClient { BaseAddress = BaseAddress };
            _ownsHttpClient = true;
        }

        _render = new Render(new HttpTransport(
            _httpClient,
            BaseAddress,
            options.ApiKey,
            options.RequestTimeout,
            options.MaxRetries,
            options.RetryDelay,
            options.OnRetry));
    }

    /// <summary>
    /// Internal constructor for unit tests. Allows injecting a deterministic jitter function
    /// to produce predictable backoff delays.
    /// </summary>
    internal PoliPageClient(PoliPageClientOptions options, Func<double> jitter)
        : this(options)
    {
        // Re-create _render with the injected jitter. The base ctor already validated
        // options and set up the HttpClient; we just replace the transport.
        _render = new Render(new Internal.HttpTransport(
            _httpClient,
            BaseAddress,
            options.ApiKey,
            options.RequestTimeout,
            options.MaxRetries,
            options.RetryDelay,
            options.OnRetry,
            jitter));
    }

    /// <summary>
    /// The effective base address used for all API requests.
    /// Equals <see cref="PoliPageClientOptions.BaseUrl"/> when set; otherwise
    /// <see cref="DefaultBaseAddress"/>.
    /// </summary>
    internal Uri BaseAddress { get; }

    /// <summary>
    /// Provides access to render operations: PDF, stream, preview, and document output.
    /// </summary>
    public Render Render => _render;

    /// <summary>
    /// <see langword="true"/> after <see cref="Dispose"/> has been called.
    /// </summary>
    internal bool IsDisposed => _disposed == 1;

    /// <summary>
    /// The underlying <see cref="HttpClient"/> used for API requests.
    /// Exposed internally so unit tests can verify ownership semantics.
    /// </summary>
    internal HttpClient HttpClient => _httpClient;

    /// <summary>
    /// Releases the resources used by this client.
    /// When the <see cref="HttpClient"/> was created internally (i.e.
    /// <see cref="PoliPageClientOptions.HttpClient"/> was <see langword="null"/>),
    /// it is disposed here. Caller-provided instances are left intact.
    /// </summary>
    public void Dispose()
    {
        if (System.Threading.Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        if (_ownsHttpClient)
            _httpClient.Dispose();
    }
}
