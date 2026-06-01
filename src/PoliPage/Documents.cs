using System.Globalization;
using System.Text.Json;
using PoliPage.Internal;

namespace PoliPage;

/// <summary>
/// Manages stored documents on Poli Page's CDN: retrieve metadata, regenerate
/// presigned URLs, fetch HTML previews and thumbnails, soft-delete documents.
/// Accessed via <see cref="PoliPageClient.Documents"/>.
/// </summary>
public sealed class Documents
{
    private readonly ITransport _transport;
    private readonly Func<string, CancellationToken, Task<byte[]>> _downloader;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    internal Documents(ITransport transport, Func<string, CancellationToken, Task<byte[]>> downloader)
    {
        _transport = transport;
        _downloader = downloader;
    }

    /// <summary>
    /// Retrieves the descriptor for a stored document, refreshing the presigned
    /// PDF URL. Use this when the original URL has expired (~15 min TTL).
    /// </summary>
    /// <param name="documentId">The server-issued document identifier (e.g. <c>doc_…</c>).</param>
    /// <param name="options">Optional per-call overrides (timeout, extra headers).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A fresh <see cref="DocumentDescriptor"/> with a new presigned URL.</returns>
    /// <exception cref="ArgumentException"><paramref name="documentId"/> is null, empty, or whitespace.</exception>
    /// <exception cref="PoliPageNotFoundException">The document does not exist.</exception>
    /// <exception cref="PoliPageGoneException">The document was soft-deleted.</exception>
    /// <exception cref="PoliPageException">Any other API failure.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<DocumentDescriptor> GetAsync(
        string documentId,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("documentId must not be null, empty, or whitespace.", nameof(documentId));

        var path = $"/v1/documents/{Uri.EscapeDataString(documentId)}";

        using var response = await _transport.GetAsync(
            path,
            options,
            "application/json",
            HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#pragma warning disable IL2026, IL3050
        var descriptor = JsonSerializer.Deserialize<DocumentDescriptor>(json, JsonOptions);
#pragma warning restore IL2026, IL3050
        return (descriptor ?? throw new PoliPageException(
                PoliPageErrorCode.Unknown,
                (int)response.StatusCode,
                "Documents.GetAsync: server returned 2xx with no JSON body."))
            with
        { Downloader = _downloader };
    }

    /// <summary>
    /// Soft-deletes a stored document. The presigned URL is invalidated and
    /// subsequent <see cref="GetAsync"/> calls return 410 Gone. The bytes are
    /// preserved on the CDN for the organisation's retention window.
    /// </summary>
    /// <param name="documentId">The server-issued document identifier (e.g. <c>doc_…</c>).</param>
    /// <param name="options">Optional per-call overrides (timeout, extra headers).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ArgumentException"><paramref name="documentId"/> is null, empty, or whitespace.</exception>
    /// <exception cref="PoliPageNotFoundException">The document does not exist.</exception>
    /// <exception cref="PoliPageGoneException">The document was already soft-deleted.</exception>
    /// <exception cref="PoliPageException">Any other API failure.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task DeleteAsync(
        string documentId,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("documentId must not be null, empty, or whitespace.", nameof(documentId));

        var path = $"/v1/documents/{Uri.EscapeDataString(documentId)}";

        using var response = await _transport.DeleteAsync(path, options, cancellationToken).ConfigureAwait(false);
        // SendAndMapErrorsAsync already throws on non-2xx; nothing more to do here.
        // The response body (if any) is discarded — the contract is "did it delete or not".
    }

    /// <summary>
    /// Fetches an HTML preview of a stored document plus the page count from the
    /// <c>X-Document-Page-Count</c> response header.
    /// </summary>
    /// <param name="documentId">The server-issued document identifier (e.g. <c>doc_…</c>).</param>
    /// <param name="options">Optional per-call overrides (timeout, extra headers).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>A <see cref="DocumentPreviewResult"/> with the full HTML body and page count.</returns>
    /// <exception cref="ArgumentException"><paramref name="documentId"/> is null, empty, or whitespace.</exception>
    /// <exception cref="PoliPageNotFoundException">The document does not exist.</exception>
    /// <exception cref="PoliPageGoneException">The document was soft-deleted.</exception>
    /// <exception cref="PoliPageException">Any other API failure.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<DocumentPreviewResult> PreviewAsync(
        string documentId,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("documentId must not be null, empty, or whitespace.", nameof(documentId));

        var path = $"/v1/documents/{Uri.EscapeDataString(documentId)}/preview";

        using var response = await _transport.GetAsync(
            path,
            options,
            "text/html",
            HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);

        var html = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var pageCount = ParseDocumentPageCount(response);

        return new DocumentPreviewResult
        {
            Html = html,
            PageCount = pageCount,
        };
    }

    /// <summary>
    /// Renders thumbnail images for each page of a stored document.
    /// </summary>
    /// <param name="documentId">The server-issued document identifier (e.g. <c>doc_…</c>).</param>
    /// <param name="thumbnailOptions">Width and format of the generated thumbnails.</param>
    /// <param name="options">Optional per-call overrides (timeout, extra headers).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>One <see cref="Thumbnail"/> per page of the source document.</returns>
    /// <exception cref="ArgumentException"><paramref name="documentId"/> is null, empty, or whitespace.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="thumbnailOptions"/> is <see langword="null"/>.</exception>
    /// <exception cref="PoliPageNotFoundException">The document does not exist.</exception>
    /// <exception cref="PoliPageException">Any other API failure.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public async Task<IReadOnlyList<Thumbnail>> ThumbnailsAsync(
        string documentId,
        ThumbnailOptions thumbnailOptions,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("documentId must not be null, empty, or whitespace.", nameof(documentId));
        ArgumentNullException.ThrowIfNull(thumbnailOptions);

        var path = $"/v1/documents/{Uri.EscapeDataString(documentId)}/thumbnails";
        var idempotencyKey = options?.IdempotencyKey ?? Guid.NewGuid().ToString();

        using var response = await _transport.PostAsync(
            path,
            thumbnailOptions,
            idempotencyKey,
            options,
            "application/json",
            HttpCompletionOption.ResponseContentRead,
            cancellationToken).ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#pragma warning disable IL2026, IL3050
        var envelope = JsonSerializer.Deserialize<ThumbnailsEnvelope>(json, JsonOptions);
#pragma warning restore IL2026, IL3050
        return envelope?.Thumbnails ?? Array.Empty<Thumbnail>();
    }

    private static int ParseDocumentPageCount(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("X-Document-Page-Count", out var values))
        {
            var raw = values.FirstOrDefault();
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                return n;
        }
        return 0;
    }

    // Wire envelope. Server wraps the array in { "thumbnails": [...] } so we can add
    // fields later (e.g. signed timestamps) without breaking clients.
    private sealed record ThumbnailsEnvelope
    {
        [System.Text.Json.Serialization.JsonPropertyName("thumbnails")]
        public IReadOnlyList<Thumbnail>? Thumbnails { get; init; }
    }
}
