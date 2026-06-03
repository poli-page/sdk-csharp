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

    /// <summary>
    /// Template version (semver). Optional — when <see langword="null"/> the API
    /// renders the project's current draft. Matches sdk-node's <c>version?: string</c>
    /// (see <c>sdk-node/src/types.ts:61</c>).
    /// </summary>
    [System.Text.Json.Serialization.JsonPropertyName("version")]
    public string? Version { get; init; }
}
