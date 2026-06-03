using System.Text.Json.Serialization;

namespace PoliPage;

/// <summary>
/// Configuration for <c>Documents.ThumbnailsAsync</c>. Determines the rendered
/// width, image format, JPEG quality, and (optionally) which pages to render.
/// Reference: <c>sdk-node/src/types.ts:148-157</c>.
/// </summary>
public sealed record ThumbnailOptions
{
    /// <summary>Target width of each thumbnail in pixels. Height is computed to preserve the aspect ratio.</summary>
    [JsonPropertyName("width")]
    public int Width { get; init; } = 200;

    /// <summary>Image format. Defaults to PNG.</summary>
    [JsonPropertyName("format")]
    public ThumbnailFormat Format { get; init; } = ThumbnailFormat.Png;

    /// <summary>
    /// JPEG quality 1-100. Only valid when <see cref="Format"/> is <see cref="ThumbnailFormat.Jpeg"/>.
    /// Null leaves the server-side default in place (omitted from the wire).
    /// </summary>
    [JsonPropertyName("quality")]
    public int? Quality { get; init; }

    /// <summary>
    /// Optional 1-based page numbers to render. When null, all pages are rendered (omitted from the wire).
    /// </summary>
    [JsonPropertyName("pages")]
    public IReadOnlyList<int>? Pages { get; init; }
}
