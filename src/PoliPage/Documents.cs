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

        var path = $"/documents/{Uri.EscapeDataString(documentId)}";

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
            with { Downloader = _downloader };
    }
}
