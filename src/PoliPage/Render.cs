using System.Text.Json;
using PoliPage.Internal;

namespace PoliPage;

/// <summary>
/// Groups all render operations: PDF, stream, preview, and document output.
/// Accessed via <see cref="PoliPageClient.Render"/>.
/// </summary>
public sealed class Render
{
    private readonly ITransport _transport;
    private readonly Func<string, CancellationToken, Task<byte[]>> _downloader;
    private readonly Func<string, CancellationToken, Task<Stream>> _streamDownloader;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter<Orientation>(JsonNamingPolicy.CamelCase),
        },
    };

    internal Render(
        ITransport transport,
        Func<string, CancellationToken, Task<byte[]>> downloader,
        Func<string, CancellationToken, Task<Stream>> streamDownloader)
    {
        _transport = transport;
        _downloader = downloader;
        _streamDownloader = streamDownloader;
    }

    /// <summary>
    /// Renders a stored project template to a PDF and returns the raw bytes.
    /// </summary>
    /// <param name="input">The project template reference and optional data.</param>
    /// <param name="options">Optional per-call overrides (idempotency key, timeout, extra headers).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>The raw PDF bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    /// <exception cref="PoliPageAuthException">The API returned 401 or 403.</exception>
    /// <exception cref="PoliPageNotFoundException">The API returned 404 (project, template, or version not found).</exception>
    /// <exception cref="PoliPageValidationException">The API returned 400 or 422 (invalid input).</exception>
    /// <exception cref="PoliPageRateLimitException">The API returned 429; <see cref="PoliPageRateLimitException.RetryAfter"/> exposes the server's retry hint.</exception>
    /// <exception cref="PoliPagePaymentRequiredException">The API returned 402 (organisation has unpaid invoices).</exception>
    /// <exception cref="PoliPageGoneException">The API returned 410 (resource permanently removed).</exception>
    /// <exception cref="PoliPageNetworkException">DNS, TCP, or TLS failure before the response was received.</exception>
    /// <exception cref="PoliPageException">Any other API failure, or a per-request timeout (<see cref="PoliPageErrorCode.Timeout"/>).</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<byte[]> PdfAsync(
        ProjectModeInput input,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // The Poli Page API has a single render endpoint that always returns a stored
        // DocumentDescriptor with a presigned URL. Mirrors sdk-node's render.pdf (src/render.ts:78-114).
        var descriptor = await RenderDocumentCoreAsync(input, options, cancellationToken).ConfigureAwait(false);
        return await _downloader(descriptor.PresignedPdfUrl, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Renders a stored project template to a PDF and returns a streaming
    /// <see cref="Stream"/> over the response body. The caller is responsible
    /// for disposing the returned stream, which releases the underlying
    /// HTTP response.
    /// </summary>
    /// <param name="input">The project template reference and optional data.</param>
    /// <param name="options">Optional per-call overrides (idempotency key, timeout, extra headers).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A read-only <see cref="Stream"/> that owns the underlying response. Disposing
    /// the stream releases the response and its socket. Use this overload for
    /// large PDFs where buffering the whole document into memory is wasteful.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    /// <exception cref="PoliPageException">See <see cref="PdfAsync"/> for the full mapping.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<Stream> PdfStreamAsync(
        ProjectModeInput input,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var descriptor = await RenderDocumentCoreAsync(input, options, cancellationToken).ConfigureAwait(false);
        return await _streamDownloader(descriptor.PresignedPdfUrl, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Renders a project template and stores the result on Poli Page's CDN.
    /// Returns a <see cref="DocumentDescriptor"/> with a short-lived presigned URL.
    /// Use this when you want to defer the binary download (e.g. queue the URL
    /// for a worker) or fetch metadata without paying for the bytes.
    /// </summary>
    /// <param name="input">The project template reference and optional data.</param>
    /// <param name="options">Optional per-call overrides (idempotency key, timeout, extra headers).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>
    /// A <see cref="DocumentDescriptor"/> with <see cref="DocumentDescriptor.PresignedPdfUrl"/>
    /// pointing at the stored document. Call <see cref="DocumentDescriptor.DownloadPdfAsync"/>
    /// to fetch the bytes via the SDK's header-less download transport.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    /// <exception cref="PoliPageException">See <see cref="PdfAsync"/> for the full mapping.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<DocumentDescriptor> DocumentAsync(
        ProjectModeInput input,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return RenderDocumentCoreAsync(input, options, cancellationToken);
    }

    // Shared core for PdfAsync, PdfStreamAsync, and DocumentAsync. Posts the render
    // request and parses the descriptor envelope. The Downloader closure is injected so
    // DocumentDescriptor.DownloadPdfAsync works on returned records (only callers that
    // expose the descriptor to the user need that — internal byte/stream consumers
    // call the closures directly).
    private async Task<DocumentDescriptor> RenderDocumentCoreAsync(
        ProjectModeInput input,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = options?.IdempotencyKey ?? Guid.NewGuid().ToString();

        using var response = await _transport.PostAsync(
            "/v1/render",
            input,
            idempotencyKey,
            options,
            "application/json",
            HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);

        var descriptor = await ParseDescriptorAsync(response, cancellationToken).ConfigureAwait(false);
        return descriptor with { Downloader = _downloader };
    }

    /// <summary>
    /// Renders a template (project-mode or inline) and returns a paginated HTML
    /// preview suitable for in-browser display. Unlike <see cref="PdfAsync"/>
    /// this accepts the abstract <see cref="RenderInput"/> base so callers can
    /// pass either <see cref="ProjectModeInput"/> or <see cref="InlineModeInput"/>.
    /// </summary>
    /// <param name="input">Project-mode or inline render input.</param>
    /// <param name="options">Optional per-call overrides (idempotency key, timeout, extra headers).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="PreviewResult"/> with the rendered HTML, total page count, and environment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="input"/> is <see langword="null"/>.</exception>
    /// <exception cref="PoliPageException">See <see cref="PdfAsync"/> for the full mapping.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<PreviewResult> PreviewAsync(
        RenderInput input,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        var idempotencyKey = options?.IdempotencyKey ?? Guid.NewGuid().ToString();

        using var response = await _transport.PostAsync(
            "/v1/render/preview",
            input,
            idempotencyKey,
            options,
            "application/json",
            HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#pragma warning disable IL2026, IL3050
        var preview = JsonSerializer.Deserialize<PreviewResult>(json, JsonOptions);
#pragma warning restore IL2026, IL3050
        return preview
            ?? throw new PoliPageException(
                PoliPageErrorCode.Unknown,
                (int)response.StatusCode,
                "Render.PreviewAsync: server returned 2xx with no JSON body.");
    }

    private static async Task<DocumentDescriptor> ParseDescriptorAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        // Why: Phase 8 will replace this with JsonSerializerContext (source-gen). Until then,
        // suppress IL2026/IL3050 — dynamic deserialization is only a concern for AOT-published
        // apps, not the JIT runtime the SDK currently targets.
#pragma warning disable IL2026, IL3050
        var descriptor = JsonSerializer.Deserialize<DocumentDescriptor>(json, JsonOptions);
#pragma warning restore IL2026, IL3050
        return descriptor
            ?? throw new PoliPageException(
                PoliPageErrorCode.Unknown,
                (int)response.StatusCode,
                "Render.DocumentAsync: server returned 2xx with no JSON body.");
    }
}
