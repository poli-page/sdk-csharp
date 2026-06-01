using System.Text.Json.Serialization;

namespace PoliPage;

/// <summary>A single page thumbnail, base64-encoded.</summary>
public sealed record Thumbnail
{
    /// <summary>1-based page number this thumbnail represents.</summary>
    [JsonPropertyName("pageNumber")]
    public required int PageNumber { get; init; }

    /// <summary>Rendered width in pixels.</summary>
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    /// <summary>Rendered height in pixels.</summary>
    [JsonPropertyName("height")]
    public required int Height { get; init; }

    /// <summary>Image format as returned by the server (<c>"png"</c> or <c>"jpeg"</c>).</summary>
    [JsonPropertyName("format")]
    public required string Format { get; init; }

    /// <summary>Base64-encoded image bytes.</summary>
    [JsonPropertyName("base64Data")]
    public required string Base64Data { get; init; }
}
