namespace PoliPage;

/// <summary>Render input that references a stored project template by slug + version.</summary>
public sealed record ProjectModeInput : RenderInput
{
    /// <summary>Project slug (e.g. "billing").</summary>
    [System.Text.Json.Serialization.JsonPropertyName("project")]
    public required string Project { get; init; }

    /// <summary>Template slug within the project (e.g. "invoice").</summary>
    [System.Text.Json.Serialization.JsonPropertyName("template")]
    public required string Template { get; init; }

    /// <summary>Template version (semver). Required by the API.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public required string Version { get; init; }
}
