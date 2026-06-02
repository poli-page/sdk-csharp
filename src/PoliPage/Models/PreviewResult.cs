using System.Text.Json.Serialization;

namespace PoliPage;

/// <summary>
/// HTML preview returned by <see cref="Render.PreviewAsync"/>. The full preview is a single
/// HTML document (the server inlines page breaks and per-page styling); the
/// <see cref="TotalPages"/> field reports how many pages the document spans for callers that
/// want to surface a "page X of Y" badge without re-parsing the HTML.
/// </summary>
public sealed record PreviewResult
{
    /// <summary>The rendered HTML body, ready to embed in an <c>iframe</c> or PDF preview viewer.</summary>
    [JsonPropertyName("html")]
    public required string Html { get; init; }

    /// <summary>Total page count reported by the server.</summary>
    [JsonPropertyName("totalPages")]
    public required int TotalPages { get; init; }

    /// <summary>API environment that produced the preview (e.g. <c>sandbox</c>, <c>live</c>).</summary>
    [JsonPropertyName("environment")]
    public required string Environment { get; init; }
}
