namespace PoliPage;

/// <summary>
/// Stored-document HTML preview returned by <c>Documents.PreviewAsync</c>.
/// </summary>
/// <remarks>
/// Unlike <see cref="PreviewResult"/> (returned by <see cref="Render.PreviewAsync"/>),
/// this carries the full document HTML as a single string plus the page count
/// from the <c>X-Document-Page-Count</c> response header. The split exists
/// because the storage preview endpoint serves the rendered HTML directly
/// rather than a paginated JSON envelope.
/// </remarks>
public sealed record DocumentPreviewResult
{
    /// <summary>The full HTML preview of the stored document.</summary>
    public required string Html { get; init; }

    /// <summary>Page count parsed from the <c>X-Document-Page-Count</c> response header.</summary>
    public required int PageCount { get; init; }
}
