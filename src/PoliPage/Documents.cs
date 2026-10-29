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
        Converters =
        {
            new System.Text.Json.Serialization.JsonStringEnumConverter<Orientation>(JsonNamingPolicy.CamelCase),
            new System.Text.Json.Serialization.JsonStringEnumConverter<PageFormat>(),
        },
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
    /// <exception cref="ArgumentNullException"><paramref name="documentId"/> is <see langword="null"/>.</exception>
    /// <exception cref="PoliPageNotFoundException">The document does not exist.</exception>
    /// <exception cref="PoliPageGoneException">The document was soft-deleted.</exception>
    /// <exception cref="PoliPageException">Any other API failure.</exception>
    /// <exception cref="PoliPageException">
    /// With <c>Code == PoliPageErrorCode.Aborted</c> when <paramref name="cancellationToken"/> was
    /// cancelled; the original <see cref="OperationCanceledException"/> is the
    /// <see cref="Exception.InnerException"/>.
    /// </exception>
    public async Task<DocumentDescriptor> GetAsync(
        string documentId,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Only null is refused: any other id, even empty, is URL-encoded into the path and
        // the API answers it (an empty id reaches no route: 404 NOT_FOUND).
        ArgumentNullException.ThrowIfNull(documentId);

        var path = $"/v1/documents/{Uri.EscapeDataString(documentId)}";

        using var response = await _transport.GetAsync(
            path,
            options,
            "application/json",
            cancellationToken).ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
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
    /// subsequent <see cref="GetAsync"/> calls return <c>410 DOCUMENT_GONE</c>. The bytes are
    /// preserved on the CDN for the organisation's retention window. Idempotent: deleting
    /// an already-deleted document also resolves (the API answers <c>204</c>).
    /// </summary>
    /// <param name="documentId">The server-issued document identifier (e.g. <c>doc_…</c>).</param>
    /// <param name="options">Optional per-call overrides (timeout, extra headers).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <exception cref="ArgumentNullException"><paramref name="documentId"/> is <see langword="null"/>.</exception>
    /// <exception cref="PoliPageNotFoundException">The document does not exist.</exception>
    /// <exception cref="PoliPageException">Any other API failure.</exception>
    /// <exception cref="PoliPageException">
    /// With <c>Code == PoliPageErrorCode.Aborted</c> when <paramref name="cancellationToken"/> was
    /// cancelled; the original <see cref="OperationCanceledException"/> is the
    /// <see cref="Exception.InnerException"/>.
    /// </exception>
    public async Task DeleteAsync(
        string documentId,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Only null is refused: any other id, even empty, is URL-encoded into the path and
        // the API answers it (an empty id reaches no route: 404 NOT_FOUND).
        ArgumentNullException.ThrowIfNull(documentId);

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
    /// <exception cref="ArgumentNullException"><paramref name="documentId"/> is <see langword="null"/>.</exception>
    /// <exception cref="PoliPageNotFoundException">The document does not exist.</exception>
    /// <exception cref="PoliPageGoneException">The document was soft-deleted.</exception>
    /// <exception cref="PoliPageException">Any other API failure.</exception>
    /// <exception cref="PoliPageException">
    /// With <c>Code == PoliPageErrorCode.Aborted</c> when <paramref name="cancellationToken"/> was
    /// cancelled; the original <see cref="OperationCanceledException"/> is the
    /// <see cref="Exception.InnerException"/>.
    /// </exception>
    public async Task<DocumentPreviewResult> PreviewAsync(
        string documentId,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Only null is refused: any other id, even empty, is URL-encoded into the path and
        // the API answers it (an empty id reaches no route: 404 NOT_FOUND).
        ArgumentNullException.ThrowIfNull(documentId);

        var path = $"/v1/documents/{Uri.EscapeDataString(documentId)}/preview";

        using var response = await _transport.GetAsync(
            path,
            options,
            "text/html",
            cancellationToken).ConfigureAwait(false);

        var html = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
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
    /// <param name="thumbnailOptions">
    /// Optional width, format, quality and pages. <see langword="null"/>, or options with
    /// nothing set, sends <c>{}</c> so every API default applies.
    /// </param>
    /// <param name="options">Optional per-call overrides (timeout, extra headers).</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>One <see cref="Thumbnail"/> per page of the source document.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="documentId"/> is <see langword="null"/>.</exception>
    /// <exception cref="PoliPageNotFoundException">The document does not exist.</exception>
    /// <exception cref="PoliPageException">Any other API failure.</exception>
    /// <exception cref="PoliPageException">
    /// With <c>Code == PoliPageErrorCode.Aborted</c> when <paramref name="cancellationToken"/> was
    /// cancelled; the original <see cref="OperationCanceledException"/> is the
    /// <see cref="Exception.InnerException"/>.
    /// </exception>
    public async Task<IReadOnlyList<Thumbnail>> ThumbnailsAsync(
        string documentId,
        ThumbnailOptions? thumbnailOptions = null,
        RequestOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // Only null is refused: any other id, even empty, is URL-encoded into the path and
        // the API answers it (an empty id reaches no route: 404 NOT_FOUND).
        ArgumentNullException.ThrowIfNull(documentId);

        var path = $"/v1/documents/{Uri.EscapeDataString(documentId)}/thumbnails";
        var idempotencyKey = options?.IdempotencyKey ?? Guid.NewGuid().ToString();

        // Why: the deployed API expects { "thumbnails": { ... } } so the server can extend the
        // envelope with non-option fields later (e.g. signed timestamps) without breaking clients.
        // With no option set the body is {} and every API default applies.
        using var response = await _transport.PostAsync(
            path,
            new ThumbnailsRequest(HasAnyOption(thumbnailOptions) ? thumbnailOptions : null),
            idempotencyKey,
            options,
            "application/json",
            cancellationToken).ConfigureAwait(false);

        var json = await response.Content.ReadAsStringAsync(CancellationToken.None).ConfigureAwait(false);
#pragma warning disable IL2026, IL3050
        var envelope = JsonSerializer.Deserialize<ThumbnailsEnvelope>(json, JsonOptions);
#pragma warning restore IL2026, IL3050
        return envelope?.Thumbnails ?? Array.Empty<Thumbnail>();
    }

    private static bool HasAnyOption(ThumbnailOptions? o)
        => o is not null && (o.Width is not null || o.Format is not null || o.Quality is not null || o.Pages is not null);

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

    // Wire request envelope. Server expects the options nested under "thumbnails" so the
    // wire shape can grow (e.g. signed timestamps) without breaking older clients. A null
    // Thumbnails is omitted by the transport serializer, leaving {}.
    private sealed record ThumbnailsRequest(
        [property: System.Text.Json.Serialization.JsonPropertyName("thumbnails")] ThumbnailOptions? Thumbnails);
}
