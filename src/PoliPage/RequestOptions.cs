namespace PoliPage;

/// <summary>Per-call overrides for a single SDK operation.</summary>
public sealed record RequestOptions
{
    /// <summary>Override the auto-generated Idempotency-Key UUID for this request.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Override the client-level per-request timeout.</summary>
    public TimeSpan? RequestTimeout { get; init; }

    /// <summary>Additional headers to send with this request only.</summary>
    public IDictionary<string, string>? Headers { get; init; }
}
