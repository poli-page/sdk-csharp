using System.Text.Json;

namespace PoliPage.Internal;

internal static class ErrorParsing
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // Code for an API error body that is JSON but carries neither `code` nor `error`.
    private const string UnknownErrorCode = "unknown_error";

    // The API's error body: { error, detail?, issues?, requestId }. `code` and `message`
    // are older spellings still read for compatibility.
    private sealed class ErrorEnvelope
    {
        public string? Code { get; set; }
        public string? Error { get; set; }
        public string? Detail { get; set; }
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? RequestId { get; set; }
    }

    /// <summary>
    /// Parses a non-2xx <see cref="HttpResponseMessage"/> into a <see cref="PoliPageException"/> subclass.
    /// Reads the response body (buffered); the caller must not have consumed it already.
    /// Does not dispose the response.
    /// </summary>
    internal static async Task<PoliPageException> FromResponseAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        ErrorEnvelope? envelope = null;
        var isJson = false;
        try
        {
            var rawBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(rawBody) && LooksLikeJson(rawBody))
            {
                // Why: Phase 8 will replace this with JsonSerializerContext (source-gen) for full
                // AOT/trim safety. Until then, suppress IL2026/IL3050 — dynamic deserialization is
                // only a concern for AOT-published apps, not the JIT runtime the SDK currently targets.
#pragma warning disable IL2026, IL3050
                envelope = JsonSerializer.Deserialize<ErrorEnvelope>(rawBody, JsonOptions);
#pragma warning restore IL2026, IL3050
                isJson = true;
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // Swallow: malformed JSON, IO flakiness, or unexpected content must not mask the HTTP
            // failure. OperationCanceledException is explicitly NOT caught here — caller cancellation
            // (or timeout-token propagation) must escape unchanged so the transport can surface the
            // right wrapper exception type.
        }

        var status = (int)response.StatusCode;
        // The code is read from the body, never inferred from the status: a body that
        // is not JSON (empty, HTML from a proxy, malformed) gives INTERNAL_ERROR.
        var code = isJson
            ? envelope?.Code ?? envelope?.Error ?? UnknownErrorCode
            : PoliPageErrorCode.InternalError;
        // RFC 7807: prefer `detail` (specific reason) over `title` (generic name)
        // over the legacy `message` field. No "API error (NNN): CODE" synthesis.
        var message = envelope?.Detail
            ?? envelope?.Title
            ?? envelope?.Message
            ?? $"HTTP {status}";
        var requestId = envelope?.RequestId
            ?? (response.Headers.TryGetValues("X-Request-Id", out var v) ? v.FirstOrDefault() : null);

        return Map(status, code, message, requestId, response);
    }

    private static PoliPageException Map(
        int status, string code, string message, string? requestId, HttpResponseMessage response)
    {
        return status switch
        {
            400 or 422 => new PoliPageValidationException(code, status, message, requestId),
            401 or 403 => new PoliPageAuthException(code, status, message, requestId),
            404 => new PoliPageNotFoundException(code, status, message, requestId),
            410 => new PoliPageGoneException(code, status, message, requestId),
            429 => new PoliPageRateLimitException(code, status, message, requestId, ParseRetryAfter(response)),
            _ => new PoliPageException(code, status, message, requestId),
        };
    }

    private static bool LooksLikeJson(string s)
    {
        var trimmed = s.AsSpan().TrimStart();
        return trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[');
    }

    /// <summary>
    /// Parses the <c>Retry-After</c> response header.
    /// Accepts both delta-seconds (e.g. <c>30</c>) and HTTP-date forms.
    /// Result is capped at 30 seconds per the SDK specification.
    /// </summary>
    internal static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header is null) return null;

        if (header.Delta is { } delta)
        {
            return delta > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : delta;
        }

        if (header.Date is { } date)
        {
            var diff = date - DateTimeOffset.UtcNow;
            if (diff <= TimeSpan.Zero) return TimeSpan.Zero;
            return diff > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : diff;
        }

        return null;
    }
}
