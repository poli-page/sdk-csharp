namespace PoliPage;

/// <summary>
/// Input for a render: a stored project template plus the data to fill it with.
/// The API renders the project's copy published in the environment of the API key;
/// there is no version to pick and no inline-HTML mode.
/// </summary>
/// <remarks>
/// The request body holds only the seven documented fields: <see cref="Project"/>,
/// <see cref="Template"/>, <see cref="Data"/>, <see cref="Metadata"/>,
/// <see cref="Format"/>, <see cref="Orientation"/> and <see cref="Locale"/>.
/// Unset optional fields are omitted, not sent as <see langword="null"/>.
/// </remarks>
public sealed record RenderInput
{
    /// <summary>Project slug (e.g. "billing").</summary>
    [System.Text.Json.Serialization.JsonPropertyName("project")]
    public required string Project { get; init; }

    /// <summary>Template slug within the project (e.g. "invoice").</summary>
    [System.Text.Json.Serialization.JsonPropertyName("template")]
    public required string Template { get; init; }

    /// <summary>Data passed to the template at render time.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("data")]
    public required object Data { get; init; }

    /// <summary>Optional primitive-only metadata (string/number/bool/null values).</summary>
    [System.Text.Json.Serialization.JsonPropertyName("metadata")]
    public RenderMetadata? Metadata { get; init; }

    /// <summary>
    /// Optional page format override. Serialised as the canonical PascalCase wire
    /// literal (e.g. <c>"A4"</c>).
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("format")]
    public PageFormat? Format { get; init; }

    /// <summary>
    /// Optional page orientation override. Serialised as the canonical lowercase
    /// wire literal (e.g. <c>"portrait"</c>).
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("orientation")]
    public Orientation? Orientation { get; init; }

    /// <summary>
    /// Optional BCP 47 locale (e.g. <c>"en-US"</c>, <c>"fr-FR"</c>) used by the
    /// renderer for page numbers and date/number formatting.
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("locale")]
    public string? Locale { get; init; }

    /// <summary>
    /// Optional override for the SDK's auto-generated Idempotency-Key UUID.
    /// Sent as the <c>Idempotency-Key</c> HTTP header, never as a body field.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? IdempotencyKey { get; init; }
}
