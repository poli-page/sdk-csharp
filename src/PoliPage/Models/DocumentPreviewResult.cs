namespace PoliPage;

/// <summary>
/// Stored-document HTML preview returned by <c>Documents.PreviewAsync</c>.
/// </summary>
/// <remarks>
/// Carries the stored HTML of a rendered document as a single string plus the
/// page count from the <c>X-Document-Page-Count</c> response header: the
/// endpoint serves the HTML directly rather than a JSON envelope.
/// </remarks>
public sealed record DocumentPreviewResult
{
    /// <summary>The full HTML preview of the stored document.</summary>
    public required string Html { get; init; }

    /// <summary>Page count parsed from the <c>X-Document-Page-Count</c> response header.</summary>
    public required int PageCount { get; init; }
}
