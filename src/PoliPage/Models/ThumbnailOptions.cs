using System.Text.Json.Serialization;

namespace PoliPage;

/// <summary>
/// Options for <c>Documents.ThumbnailsAsync</c>: width, image format, JPEG quality
/// and which pages to render. Every option is optional; an unset option is not
/// sent and the API default applies. The SDK does not check bounds: the API
/// answers <c>400 VALIDATION_ERROR</c> for an out-of-range value.
/// </summary>
public sealed record ThumbnailOptions
{
    /// <summary>
    /// Width of each thumbnail in pixels (50-2000; API default 400). Height is computed
    /// to preserve the aspect ratio.
    /// </summary>
    [JsonPropertyName("width")]
    public int? Width { get; init; }

    /// <summary>Image format (API default <see cref="ThumbnailFormat.Jpeg"/>).</summary>
    [JsonPropertyName("format")]
    public ThumbnailFormat? Format { get; init; }

    /// <summary>
    /// JPEG quality 1-100. Only valid when <see cref="Format"/> is <see cref="ThumbnailFormat.Jpeg"/>.
    /// </summary>
    [JsonPropertyName("quality")]
    public int? Quality { get; init; }

    /// <summary>
    /// Optional 1-based page numbers to render. When null, all pages are rendered.
    /// </summary>
    [JsonPropertyName("pages")]
    public IReadOnlyList<int>? Pages { get; init; }
}
