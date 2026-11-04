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
    private readonly TimeSpan _requestTimeout;
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
            throw new PoliPageException(PoliPageErrorCode.InvalidOptions, statusCode: 0, SdkErrors.InvalidOptionsMessage);

        if (options.MaxRetries < 0)
            throw new PoliPageException(PoliPageErrorCode.InvalidOptions, statusCode: 0, "PoliPage: MaxRetries must be ≥ 0.");

        if (options.RetryDelay <= TimeSpan.Zero)
            throw new PoliPageException(PoliPageErrorCode.InvalidOptions, statusCode: 0, "PoliPage: RetryDelay must be > 0.");

        if (options.RequestTimeout <= TimeSpan.Zero)
            throw new PoliPageException(PoliPageErrorCode.InvalidOptions, statusCode: 0, "PoliPage: RequestTimeout must be > 0.");

        _options = options;
        _requestTimeout = options.RequestTimeout;
        BaseAddress = options.BaseUrl ?? DefaultBaseAddress;

        (_httpClient, _ownsHttpClient) = options.HttpClient is not null
            ? (options.HttpClient, false)
            : (CreateApiHttpClient(BaseAddress), true);

        // Why: presigned S3 URLs reject requests that carry an Authorization header alongside
        // the URL signature, and some object stores 400 on an unexpected User-Agent. The
        // download path uses its own bare HttpClient so the SDK's API auth/UA can never leak.
        // No HttpClient.Timeout of its own: RequestTimeout bounds the download.
        (_downloadHttp, _ownsDownloadHttp) = options.DownloadHttpClient is not null
            ? (options.DownloadHttpClient, false)
            : (new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan }, true);

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
        _documents = new Documents(transport, (url, cancellationToken) => DownloadAsync(url, null, cancellationToken));
    }

    // No HttpClient.Timeout of its own (it defaults to 100 s): RequestTimeout bounds each
    // attempt, whatever its value.
    private static HttpClient CreateApiHttpClient(Uri baseAddress)
        => new() { BaseAddress = baseAddress, Timeout = System.Threading.Timeout.InfiniteTimeSpan };

    /// <summary>
    /// The effective base address used for all API requests.
    /// Equals <see cref="PoliPageClientOptions.BaseUrl"/> when set; otherwise
    /// <see cref="DefaultBaseAddress"/>.
    /// </summary>
    internal Uri BaseAddress { get; }

    /// <summary>
    /// Provides access to render operations: PDF bytes, PDF stream, and stored document.
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
    /// <paramref name="timeout"/> (else the client's <see cref="PoliPageClientOptions.RequestTimeout"/>)
    /// bounds the whole download, body included. The download is never retried.
    /// </summary>
    internal async Task<byte[]> DownloadAsync(string url, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(timeout ?? _requestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var token = linkedCts.Token;

        using var request = BuildDownloadRequest(url);
        using var response = await SendDownloadAsync(request, timeoutCts, token, cancellationToken).ConfigureAwait(false);

        ThrowIfDownloadFailed(response);

        try
        {
            return await HttpTransport.BoundAsync(response.Content.ReadAsByteArrayAsync(token), token).ConfigureAwait(false);
        }
        catch (Exception ex) when (SdkErrors.IsTransportFailure(ex) || cancellationToken.IsCancellationRequested)
        {
            // A connection cut, a body short of its Content-Length or a timeout mid-body:
            // the SDK's own error, never the raw one.
            throw DownloadFailure(ex, timeoutCts.IsCancellationRequested, cancellationToken);
        }
    }

    // A plain GET to object storage: no Authorization, no Idempotency-Key.
    private static HttpRequestMessage BuildDownloadRequest(string url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        NoHiddenResend.Apply(request);
        return request;
    }

    /// <summary>
    /// The error for a download that got no complete answer: <c>aborted</c> when the
    /// caller cancelled; <c>DOWNLOAD_FAILED</c> without a status otherwise, with the message
    /// <c>Failed to download PDF: timeout</c> when a timeout fired (the SDK's, as
    /// <paramref name="timedOut"/> reports it, or the download HttpClient's own
    /// <see cref="System.Net.Http.HttpClient.Timeout"/>), and
    /// <c>Failed to download PDF: network error</c> for anything else (a connection, DNS or
    /// TLS failure, a connection cut mid-body).
    /// </summary>
    internal static PoliPageException DownloadFailure(Exception ex, bool timedOut, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return SdkErrors.Aborted(ex);
        if (timedOut || ex is TaskCanceledException { InnerException: TimeoutException })
            return SdkErrors.DownloadTimeout(ex);
        return SdkErrors.DownloadNetwork(ex);
    }

    /// <summary>
    /// Sends a request on the dedicated download transport, bounded by
    /// <paramref name="token"/> (the caller's cancellation and <paramref name="timeoutCts"/>)
    /// whatever the download HttpClient's handler does with it. Transport-level failures
    /// (DNS, connection refused, TLS, a timeout) become the SDK's typed
    /// <see cref="PoliPageDownloadException"/> so callers never see a raw
    /// <see cref="HttpRequestException"/>, and caller cancellation becomes <c>aborted</c>.
    /// </summary>
    private async Task<HttpResponseMessage> SendDownloadAsync(
        HttpRequestMessage request,
        CancellationTokenSource timeoutCts,
        CancellationToken token,
        CancellationToken cancellationToken)
    {
        try
        {
            var send = _downloadHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            return await HttpTransport.BoundAsync(send, token).ConfigureAwait(false);
        }
        catch (Exception ex) when (SdkErrors.IsTransportFailure(ex) || cancellationToken.IsCancellationRequested)
        {
            throw DownloadFailure(ex, timeoutCts.IsCancellationRequested, cancellationToken);
        }
    }

    /// <summary>
    /// Streaming variant of <see cref="DownloadAsync"/>. Returns a <see cref="Stream"/>
    /// that owns the underlying <see cref="HttpResponseMessage"/> so the caller can
    /// dispose both with a single <c>using</c>. Used by <see cref="Render.PdfStreamAsync"/>.
    /// <paramref name="timeout"/> (else the client's <see cref="PoliPageClientOptions.RequestTimeout"/>)
    /// is an idle timeout: it bounds the wait for the storage's answer, then each read of
    /// the stream, not the total. Its read failures are SDK errors too: <c>aborted</c> or
    /// <c>DOWNLOAD_FAILED</c>.
    /// </summary>
    internal async Task<Stream> DownloadStreamAsync(string url, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var idleTimeout = timeout ?? _requestTimeout;
        using var timeoutCts = new CancellationTokenSource(idleTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var token = linkedCts.Token;

        var request = BuildDownloadRequest(url);
        HttpResponseMessage? response = null;
        try
        {
            response = await SendDownloadAsync(request, timeoutCts, token, cancellationToken).ConfigureAwait(false);

            ThrowIfDownloadFailed(response);

            Stream bodyStream;
            try
            {
                bodyStream = await HttpTransport.BoundAsync(response.Content.ReadAsStreamAsync(token), token)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (SdkErrors.IsTransportFailure(ex) || cancellationToken.IsCancellationRequested)
            {
                throw DownloadFailure(ex, timeoutCts.IsCancellationRequested, cancellationToken);
            }

            var owned = new ResponseOwnedStream(bodyStream, response, idleTimeout, cancellationToken);
            response = null; // ownership transferred to the stream wrapper
            return owned;
        }
        finally
        {
            response?.Dispose();
            request.Dispose();
        }
    }

    private static void ThrowIfDownloadFailed(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var requestId = response.Headers.TryGetValues("X-Request-Id", out var v)
            ? v.FirstOrDefault()
            : null;
        throw SdkErrors.DownloadStatus((int)response.StatusCode, requestId);
    }

    /// <summary>
    /// Convenience helper that renders a stored project template directly to a file
    /// on disk via <see cref="Render.PdfStreamAsync"/>, without buffering the whole PDF into
    /// memory. It creates the file's missing parent directories.
    /// </summary>
    /// <remarks>
    /// The PDF is written to a temporary file in the target's directory, which is moved over
    /// <paramref name="path"/> only once the whole PDF is written. When anything fails (the
    /// render, the download, the write or a cancellation), the target is left as it was: an
    /// existing file keeps its content and a missing one is not created. The directory and
    /// the temporary file are prepared before the render, so a path that cannot be written
    /// costs no render; so does an existing target that is not a regular file (a directory, a
    /// FIFO, a device, a socket: <c>Failed to write PDF: &lt;target&gt;: not a regular file</c>)
    /// or that the process may not write, which is <c>io_error</c> and is not replaced. The
    /// target is checked from its attributes, never opened. A <paramref name="path"/>
    /// that is a symbolic link is resolved (a relative link against the real directory that
    /// holds it): the file it points at is replaced and the link stays a link. An existing file keeps its
    /// permissions on Unix, but the move gives it a new inode: its owner becomes the writing
    /// user, and a hard link to the old file keeps the old content.
    /// </remarks>
    /// <param name="input">The project, template and data to render.</param>
    /// <param name="path">Destination file path. Existing files are overwritten.</param>
    /// <param name="options">Optional per-call overrides (idempotency key, timeout, extra headers).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is <see langword="null"/>, empty, or whitespace.</exception>
    /// <exception cref="PoliPageException">
    /// With <c>Code == PoliPageErrorCode.IoError</c> when the file cannot be written (creating
    /// its directory, opening, writing or moving it into place): the message is
    /// <c>Failed to write PDF: &lt;reason&gt;</c>, with the file system's own text, and the
    /// file system's exception is the <see cref="Exception.InnerException"/>. Never retried.
    /// </exception>
    /// <exception cref="PoliPageException">See <see cref="Render.PdfStreamAsync"/> for the other failures.</exception>
    public async Task RenderToFileAsync(
        RenderInput input,
        string path,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path must not be null, empty, or whitespace.", nameof(path));

        var file = AtomicFileWriter.Open(path);
        await using (file.ConfigureAwait(false))
        {
            var pdf = await Render.PdfStreamAsync(input, options, cancellationToken).ConfigureAwait(false);
            await using (pdf.ConfigureAwait(false))
            {
                try
                {
                    await pdf.CopyToAsync(file.Stream, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
                {
                    // The stream's copy already maps a cancelled read or write to aborted; this
                    // keeps any other cancellation on the way out of the copy the same error.
                    throw SdkErrors.Aborted(ex);
                }
                catch (Exception ex) when (ex is not PoliPageException && SdkErrors.IsFileSystemFailure(ex))
                {
                    // The PDF's reads raise PoliPageException: this is the file's write.
                    throw SdkErrors.IoError(ex);
                }
            }

            await file.CommitAsync().ConfigureAwait(false);
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
