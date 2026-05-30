using System.Text.Json.Serialization;

namespace PoliPage;

/// <summary>
/// Configuration for <c>Documents.ThumbnailsAsync</c>. Determines the rendered
/// width and image format for the returned thumbnails.
/// </summary>
public sealed record ThumbnailOptions
{
    /// <summary>Target width of each thumbnail in pixels. Height is computed to preserve the aspect ratio.</summary>
    [JsonPropertyName("width")]
    public int Width { get; init; } = 200;

    /// <summary>Image format. Defaults to PNG.</summary>
    [JsonPropertyName("format")]
    public ThumbnailFormat Format { get; init; } = ThumbnailFormat.Png;
}
