using System.Linq;
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
    private readonly HttpClient _downloadHttp;
    private readonly bool _ownsDownloadHttp;
    private readonly Render _render;
    private readonly Documents _documents;
    private int _disposed;

    /// <summary>
    /// Initializes a new <see cref="PoliPageClient"/> with the supplied options.
    /// </summary>
    /// <param name="options">Client configuration. Must not be <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="PoliPageException">
    /// <see cref="PoliPageClientOptions.ApiKey"/> is null/empty/whitespace, or
    /// <see cref="PoliPageClientOptions.MaxRetries"/> is negative, or
    /// <see cref="PoliPageClientOptions.RetryDelay"/> or
    /// <see cref="PoliPageClientOptions.RequestTimeout"/> are not positive.
    /// Surfaces with <see cref="PoliPageException.Code"/> equal to
    /// <see cref="PoliPageErrorCode.InvalidOptions"/>.
    /// </exception>
    public PoliPageClient(PoliPageClientOptions options) : this(options, jitter: null) { }

    // Internal ctor: lets tests inject a deterministic jitter source for the retry
    // backoff math without leaking the seam onto the public surface.
    internal PoliPageClient(PoliPageClientOptions options, Func<double>? jitter)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Config validation uses PoliPageException("invalid_options") per sdk-node/src/index.ts:84-86.
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new PoliPageException(PoliPageErrorCode.InvalidOptions, statusCode: 0, "PoliPage: ApiKey is required.");

        if (options.MaxRetries < 0)
            throw new PoliPageException(PoliPageErrorCode.InvalidOptions, statusCode: 0, "PoliPage: MaxRetries must be ≥ 0.");

        if (options.RetryDelay <= TimeSpan.Zero)
            throw new PoliPageException(PoliPageErrorCode.InvalidOptions, statusCode: 0, "PoliPage: RetryDelay must be > 0.");

        if (options.RequestTimeout <= TimeSpan.Zero)
            throw new PoliPageException(PoliPageErrorCode.InvalidOptions, statusCode: 0, "PoliPage: RequestTimeout must be > 0.");

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

        // Why: presigned S3 URLs reject requests that carry an Authorization header alongside
        // the URL signature, and some object stores 400 on an unexpected User-Agent. The
        // download path uses its own bare HttpClient so the SDK's API auth/UA can never leak.
        if (options.DownloadHttpClient is not null)
        {
            _downloadHttp = options.DownloadHttpClient;
            _ownsDownloadHttp = false;
        }
        else
        {
            _downloadHttp = new HttpClient();
            _ownsDownloadHttp = true;
        }

        var transport = new HttpTransport(
            _httpClient,
            BaseAddress,
            options.ApiKey,
            options.RequestTimeout,
            options.MaxRetries,
            options.RetryDelay,
            options.OnRetry,
            options.OnError,
            options.OnRequest,
            options.OnResponse,
            jitter);
        _render = new Render(transport, DownloadAsync, DownloadStreamAsync);
        _documents = new Documents(transport, DownloadAsync);
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
    /// Provides access to document-management operations: retrieve, preview,
    /// thumbnails, soft-delete.
    /// </summary>
    public Documents Documents => _documents;

    /// <summary>
    /// <see langword="true"/> after <see cref="Dispose"/> has been called.
    /// </summary>
    internal bool IsDisposed => _disposed == 1;

    /// <summary>
    /// The underlying <see cref="HttpClient"/> used for API requests.
    /// Exposed internally so unit tests can verify ownership semantics.
    /// </summary>
    internal HttpClient HttpClient => _httpClient;

    /// <summary>The header-less HttpClient used for presigned downloads.</summary>
    internal HttpClient DownloadHttpClient => _downloadHttp;

    /// <summary>
    /// Fetches the bytes at <paramref name="url"/> via the SDK's dedicated, header-less
    /// download transport. Used by <see cref="DocumentDescriptor.DownloadPdfAsync"/> and
    /// by <see cref="Render.PdfAsync"/> for the post-render PDF fetch step.
    /// </summary>
    internal async Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        using var response = await _downloadHttp
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        await ThrowIfDownloadFailedAsync(response).ConfigureAwait(false);

        return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Streaming variant of <see cref="DownloadAsync"/>. Returns a <see cref="Stream"/>
    /// that owns the underlying <see cref="HttpResponseMessage"/> so the caller can
    /// dispose both with a single <c>using</c>. Used by <see cref="Render.PdfStreamAsync"/>.
    /// </summary>
    internal async Task<Stream> DownloadStreamAsync(string url, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        HttpResponseMessage? response = null;
        try
        {
            response = await _downloadHttp
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            await ThrowIfDownloadFailedAsync(response).ConfigureAwait(false);

            var bodyStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var owned = new ResponseOwnedStream(bodyStream, response);
            response = null; // ownership transferred to the stream wrapper
            return owned;
        }
        finally
        {
            response?.Dispose();
            request.Dispose();
        }
    }

    private static Task ThrowIfDownloadFailedAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return Task.CompletedTask;

        var requestId = response.Headers.TryGetValues("X-Request-Id", out var v)
            ? v.FirstOrDefault()
            : null;
        throw new PoliPageDownloadException(
            PoliPageErrorCode.DownloadFailed,
            (int)response.StatusCode,
            $"Presigned download failed: HTTP {(int)response.StatusCode}",
            requestId);
    }

    /// <summary>
    /// Convenience helper that renders a stored project template directly to a file
    /// on disk via <see cref="Render.PdfStreamAsync"/> + <see cref="Stream.CopyToAsync(Stream, CancellationToken)"/>.
    /// Streams without buffering the whole PDF into memory.
    /// </summary>
    /// <param name="input">The project template reference and optional data.</param>
    /// <param name="path">Destination file path. Existing files are overwritten.</param>
    /// <param name="options">Optional per-call overrides (idempotency key, timeout, extra headers).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <exception cref="PoliPageException">See <see cref="Render.PdfAsync"/> for the full mapping.</exception>
    /// <exception cref="IOException">The file at <paramref name="path"/> cannot be created or written.</exception>
    public async Task RenderToFileAsync(
        ProjectModeInput input,
        string path,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be null, empty, or whitespace.", nameof(path));

        var pdf = await Render.PdfStreamAsync(input, options, cancellationToken).ConfigureAwait(false);
        await using (pdf.ConfigureAwait(false))
        {
            var file = File.Create(path);
            await using (file.ConfigureAwait(false))
            {
                await pdf.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }
        }
    }

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
        if (_ownsDownloadHttp)
            _downloadHttp.Dispose();
    }
}
