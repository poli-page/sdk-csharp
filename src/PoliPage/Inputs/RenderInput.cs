namespace PoliPage;

/// <summary>
/// Base type for render inputs. Sealed against external extension via an
/// internal constructor — only <see cref="ProjectModeInput"/> and
/// <see cref="InlineModeInput"/> can derive.
/// </summary>
public abstract record RenderInput
{
    /// <summary>Free-form data passed to the template at render time.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("data")]
    public object? Data { get; init; }

    /// <summary>
    /// Optional page format override. Serialised as the canonical PascalCase wire
    /// literal (e.g. <c>"A4"</c>). Reference: <c>sdk-node/src/types.ts:37</c>.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("format")]
    public PageFormat? Format { get; init; }

    /// <summary>
    /// Optional page orientation override. Serialised as the canonical lowercase
    /// wire literal (e.g. <c>"portrait"</c>). Reference: <c>sdk-node/src/types.ts:39</c>.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("orientation")]
    public Orientation? Orientation { get; init; }

    /// <summary>
    /// Optional BCP 47 locale (e.g. <c>"en-US"</c>, <c>"fr-FR"</c>) used by the
    /// renderer for page numbers and date/number formatting.
    /// Reference: <c>sdk-node/src/types.ts:41</c>.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("locale")]
    public string? Locale { get; init; }

    /// <summary>Optional primitive-only metadata (string/number/bool/null values).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("metadata")]
    public RenderMetadata? Metadata { get; init; }

    /// <summary>
    /// Optional override for the SDK's auto-generated Idempotency-Key UUID.
    /// Sent as a HTTP header, NOT a body field — <see cref="System.Text.Json.Serialization.JsonIgnoreAttribute"/>
    /// keeps it off the wire body. Reference: <c>sdk-node/src/types.ts:51</c>.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? IdempotencyKey { get; init; }

    // Why: internal ctor intentionally blocks external derivation (Meziantou MA0017 suppressed
    // because this is a deliberate API constraint, not an oversight).
#pragma warning disable MA0017
    internal RenderInput() { }
#pragma warning restore MA0017
}
