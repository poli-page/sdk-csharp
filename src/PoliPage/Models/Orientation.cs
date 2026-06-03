namespace PoliPage;

/// <summary>
/// Page orientation for rendered output. Serialised as a lowercase wire
/// literal — <c>Orientation.Portrait</c> → <c>"portrait"</c>.
/// </summary>
/// <remarks>
/// Reference: <c>sdk-node/src/types.ts:21</c>.
/// </remarks>
public enum Orientation
{
    /// <summary>Portrait orientation — height &gt; width.</summary>
    Portrait,

    /// <summary>Landscape orientation — width &gt; height.</summary>
    Landscape,
}
