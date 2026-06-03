namespace PoliPage;

/// <summary>
/// Canonical Poli Page page formats. Serialised verbatim (PascalCase) to the
/// wire — <c>PageFormat.A4</c> → <c>"A4"</c>, <c>PageFormat.Letter</c> → <c>"Letter"</c>.
/// </summary>
/// <remarks>
/// The contract is documented in the platform spec and must match every other SDK
/// (reference: <c>sdk-node/src/types.ts:7-19</c>).
/// </remarks>
public enum PageFormat
{
    /// <summary>ISO 216 A3 (297 × 420 mm).</summary>
    A3,
    /// <summary>ISO 216 A4 (210 × 297 mm). Most common in EU/APAC.</summary>
    A4,
    /// <summary>ISO 216 A5 (148 × 210 mm).</summary>
    A5,
    /// <summary>ISO 216 A6 (105 × 148 mm).</summary>
    A6,
    /// <summary>ISO 216 B4 (250 × 353 mm).</summary>
    B4,
    /// <summary>ISO 216 B5 (176 × 250 mm).</summary>
    B5,
    /// <summary>US Letter (8.5 × 11 in).</summary>
    Letter,
    /// <summary>US Legal (8.5 × 14 in).</summary>
    Legal,
    /// <summary>US Tabloid (11 × 17 in).</summary>
    Tabloid,
    /// <summary>US Executive (7.25 × 10.5 in).</summary>
    Executive,
    /// <summary>US Statement (5.5 × 8.5 in).</summary>
    Statement,
    /// <summary>US Folio / Foolscap (8.5 × 13 in).</summary>
    Folio,
}
