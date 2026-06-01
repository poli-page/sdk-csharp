using System.Text.Json.Serialization;

namespace PoliPage;

/// <summary>
/// Paginated HTML preview returned by <see cref="Render.PreviewAsync"/>. Each
/// element of <see cref="Pages"/> is the rendered HTML for one page of the
/// template, suitable for embedding in an iframe or PDF preview viewer.
/// </summary>
public sealed record PreviewResult
{
    /// <summary>HTML for each page of the rendered preview, in order.</summary>
    [JsonPropertyName("pages")]
    public required IReadOnlyList<string> Pages { get; init; }

    /// <summary>Total page count reported by the server (matches <see cref="Pages"/>.Count).</summary>
    [JsonPropertyName("totalPageCount")]
    public required int TotalPageCount { get; init; }
}
