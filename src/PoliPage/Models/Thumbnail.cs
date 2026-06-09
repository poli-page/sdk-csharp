using System.Text.Json.Serialization;

namespace PoliPage;

/// <summary>A single page thumbnail, base64-encoded.</summary>
public sealed record Thumbnail
{
    /// <summary>1-based page number this thumbnail represents.</summary>
    [JsonPropertyName("page")]
    public required int Page { get; init; }

    /// <summary>Rendered width in pixels.</summary>
    [JsonPropertyName("width")]
    public required int Width { get; init; }

    /// <summary>Rendered height in pixels.</summary>
    [JsonPropertyName("height")]
    public required int Height { get; init; }

    /// <summary>Image MIME type as returned by the server (e.g., <c>"image/png"</c>, <c>"image/jpeg"</c>).</summary>
    [JsonPropertyName("contentType")]
    public required string ContentType { get; init; }

    /// <summary>Base64-encoded image bytes.</summary>
    [JsonPropertyName("data")]
    public required string Data { get; init; }
}
