namespace PoliPage;

/// <summary>
/// Free-form metadata attached to a render. Values must be primitive
/// (string, number, bool, or null) — non-primitive values throw at send-time.
/// </summary>
public sealed class RenderMetadata : Dictionary<string, object?>
{
    /// <summary>Initializes an empty <see cref="RenderMetadata"/>.</summary>
    public RenderMetadata() { }

    /// <summary>Initializes a <see cref="RenderMetadata"/> populated from an existing dictionary.</summary>
    /// <param name="source">The source dictionary to copy entries from.</param>
    public RenderMetadata(IDictionary<string, object?> source) : base(source) { }
}
