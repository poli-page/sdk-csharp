namespace PoliPage;

/// <summary>Image format for a generated thumbnail.</summary>
/// <remarks>
/// Serialised as a camelCase lowercase string (<c>"png"</c> / <c>"jpeg"</c>)
/// by the SDK's wire serializer.
/// </remarks>
public enum ThumbnailFormat
{
    /// <summary>PNG — lossless, supports transparency.</summary>
    Png,

    /// <summary>JPEG — lossy, smaller payloads at the cost of quality.</summary>
    Jpeg,
}
