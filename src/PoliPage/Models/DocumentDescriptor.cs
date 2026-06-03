using System.Text.Json.Serialization;

namespace PoliPage;

/// <summary>
/// Server-side description of a rendered, stored PDF document. Returned by
/// <see cref="Render.DocumentAsync"/> and <c>Documents.GetAsync</c>.
/// </summary>
/// <remarks>
/// <para>
/// The presigned URL in <see cref="PresignedPdfUrl"/> has a short TTL (~15 min).
/// Call <see cref="DownloadPdfAsync"/> to fetch the bytes through the SDK's
/// dedicated download transport, which does NOT carry the SDK's API auth
/// headers — required because S3 rejects unexpected <c>Authorization</c>
/// alongside the presigned signature.
/// </para>
/// <para>
/// Descriptors constructed manually (not produced by a <see cref="PoliPageClient"/>)
/// cannot call <see cref="DownloadPdfAsync"/> — the SDK-injected downloader is
/// internal-only and never present on a hand-built record.
/// </para>
/// </remarks>
public sealed record DocumentDescriptor
{
    /// <summary>Server-issued document identifier (e.g. <c>doc_…</c>).</summary>
    [JsonPropertyName("documentId")] public required string DocumentId { get; init; }

    /// <summary>Server-issued organisation identifier (e.g. <c>org_…</c>).</summary>
    [JsonPropertyName("organizationId")] public required string OrganizationId { get; init; }

    /// <summary>Project identifier when the render referenced a project template.</summary>
    [JsonPropertyName("projectId")] public string? ProjectId { get; init; }

    /// <summary>Project slug when the render referenced a project template.</summary>
    [JsonPropertyName("projectSlug")] public string? ProjectSlug { get; init; }

    /// <summary>Template identifier when the render referenced a project template.</summary>
    [JsonPropertyName("templateId")] public string? TemplateId { get; init; }

    /// <summary>Template slug when the render referenced a project template.</summary>
    [JsonPropertyName("templateSlug")] public string? TemplateSlug { get; init; }

    /// <summary>Template version (semver) when the render referenced a project template.</summary>
    [JsonPropertyName("version")] public string? Version { get; init; }

    /// <summary>API environment (e.g. <c>live</c>, <c>test</c>).</summary>
    [JsonPropertyName("environment")] public required string Environment { get; init; }

    /// <summary>Identifier of the API key used to create the document, when available.</summary>
    [JsonPropertyName("apiKeyId")] public string? ApiKeyId { get; init; }

    /// <summary>Document format (e.g. <c>pdf</c>).</summary>
    [JsonPropertyName("format")] public required string Format { get; init; }

    /// <summary>Page orientation at render time, when set. Null when the API does not surface it.</summary>
    [JsonPropertyName("orientation")] public Orientation? Orientation { get; init; }

    /// <summary>BCP 47 locale used at render time (e.g. <c>fr-FR</c>), when set.</summary>
    [JsonPropertyName("locale")] public string? Locale { get; init; }

    /// <summary>Number of pages in the rendered document.</summary>
    [JsonPropertyName("pageCount")] public required int PageCount { get; init; }

    /// <summary>Size of the rendered document on disk in bytes.</summary>
    [JsonPropertyName("sizeBytes")] public required long SizeBytes { get; init; }

    /// <summary>Server timestamp when the document was rendered and stored.</summary>
    [JsonPropertyName("createdAt")] public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Time-limited presigned URL for direct PDF download (~15 min TTL).</summary>
    [JsonPropertyName("presignedPdfUrl")] public required string PresignedPdfUrl { get; init; }

    /// <summary>Server timestamp when the presigned PDF URL expires (~15 min after <see cref="CreatedAt"/>).</summary>
    [JsonPropertyName("expiresAt")] public required DateTimeOffset ExpiresAt { get; init; }

    /// <summary>Caller-supplied primitive-only metadata, round-tripped from the original render.</summary>
    [JsonPropertyName("metadata")] public RenderMetadata? Metadata { get; init; }

    // Why: [JsonIgnore] is load-bearing. Without it, JsonSerializer round-trips drop the
    // closure on null; we'd then re-inject it via `with { Downloader = … }` after parse.
    // External callers cannot set this — `internal init` on a public record.
    [JsonIgnore]
    internal Func<string, CancellationToken, Task<byte[]>>? Downloader { get; init; }

    /// <summary>
    /// Fetches the PDF bytes from <see cref="PresignedPdfUrl"/> via the SDK's
    /// dedicated, header-less download transport.
    /// </summary>
    /// <param name="cancellationToken">Token to cancel the download.</param>
    /// <returns>The raw PDF bytes.</returns>
    /// <exception cref="InvalidOperationException">
    /// This descriptor was constructed manually and was never handed back by a
    /// <see cref="PoliPageClient"/> — no downloader is wired.
    /// </exception>
    /// <exception cref="PoliPageDownloadException">The presigned URL fetch returned a non-2xx status.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was cancelled.</exception>
    public Task<byte[]> DownloadPdfAsync(CancellationToken cancellationToken = default)
    {
        if (Downloader is null)
        {
            throw new InvalidOperationException(
                "DocumentDescriptor was not produced by a PoliPageClient — DownloadPdfAsync requires the SDK-injected downloader.");
        }
        return Downloader(PresignedPdfUrl, cancellationToken);
    }
}
