namespace PoliPage;

/// <summary>Render input carrying raw HTML inline — bypasses the project/template store.</summary>
public sealed record InlineModeInput : RenderInput
{
    /// <summary>Raw HTML to render.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("template")]
    public required string Template { get; init; }
}
