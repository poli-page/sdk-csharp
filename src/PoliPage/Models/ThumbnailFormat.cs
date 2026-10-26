namespace PoliPage;

/// <summary>Image format for a generated thumbnail.</summary>
/// <remarks>
/// Serialised as a lowercase string (<c>"png"</c>, <c>"jpeg"</c>, <c>"webp"</c>)
/// by the SDK's wire serializer.
/// </remarks>
public enum ThumbnailFormat
{
    /// <summary>PNG — lossless, supports transparency.</summary>
    Png,

    /// <summary>JPEG — lossy, smaller payloads at the cost of quality. The API default.</summary>
    Jpeg,

    /// <summary>WebP — lossy, typically smaller than JPEG at the same quality.</summary>
    Webp,
}
