using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
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
    /// Maps a non-2xx answer whose body has been read in full to a
    /// <see cref="PoliPageException"/> subclass (spec §8.2).
    /// </summary>
    internal static PoliPageException FromBody(int status, HttpResponseHeaders headers, byte[] body)
    {
        var envelope = TryParseEnvelope(body);

        // The code is read from the body, never inferred from the status: a body that
        // is not JSON (empty, HTML from a proxy, malformed) gives INTERNAL_ERROR.
        var code = envelope is null
            ? PoliPageErrorCode.InternalError
            : envelope.Code ?? envelope.Error ?? UnknownErrorCode;
        // RFC 7807: prefer `detail` (specific reason) over `title` (generic name)
        // over the legacy `message` field. No "API error (NNN): CODE" synthesis.
        var message = envelope?.Detail
            ?? envelope?.Title
            ?? envelope?.Message
            ?? HttpStatusMessage(status);
        // The x-request-id header is the request id (spec §8.1); the body's copy is the
        // fallback for an answer without the header.
        var requestId = HeaderRequestId(headers) ?? envelope?.RequestId;

        return Map(status, code, message, requestId, headers, innerException: null);
    }

    /// <summary>
    /// Maps a non-2xx answer whose body failed while it was read (a dropped connection,
    /// the attempt timeout): handled as a body that is not JSON (spec §8.2), never with a
    /// code taken from the bytes received so far. The real status and request id are kept
    /// and <paramref name="readError"/> becomes the inner exception.
    /// </summary>
    internal static PoliPageException FromUnreadableBody(int status, HttpResponseHeaders headers, Exception readError)
        => Map(
            status,
            PoliPageErrorCode.InternalError,
            HttpStatusMessage(status),
            HeaderRequestId(headers),
            headers,
            readError);

    private static ErrorEnvelope? TryParseEnvelope(byte[] body)
    {
        if (body.Length == 0)
            return null;

        var rawBody = Encoding.UTF8.GetString(body);
        if (!LooksLikeJson(rawBody))
            return null;

        try
        {
            // Why: Phase 8 will replace this with JsonSerializerContext (source-gen) for full
            // AOT/trim safety. Until then, suppress IL2026/IL3050 — dynamic deserialization is
            // only a concern for AOT-published apps, not the JIT runtime the SDK currently targets.
#pragma warning disable IL2026, IL3050
            return JsonSerializer.Deserialize<ErrorEnvelope>(rawBody, JsonOptions);
#pragma warning restore IL2026, IL3050
        }
        catch (JsonException)
        {
            // Malformed JSON, or JSON of another shape: handled as a body that is not JSON.
            return null;
        }
    }

    private static string HttpStatusMessage(int status)
        => string.Create(CultureInfo.InvariantCulture, $"HTTP {status}");

    private static string? HeaderRequestId(HttpResponseHeaders headers)
        => headers.TryGetValues("X-Request-Id", out var values) ? values.FirstOrDefault() : null;

    private static PoliPageException Map(
        int status,
        string code,
        string message,
        string? requestId,
        HttpResponseHeaders headers,
        Exception? innerException)
    {
        return status switch
        {
            400 or 422 => new PoliPageValidationException(code, status, message, requestId, innerException),
            401 or 403 => new PoliPageAuthException(code, status, message, requestId, innerException),
            404 => new PoliPageNotFoundException(code, status, message, requestId, innerException),
            410 => new PoliPageGoneException(code, status, message, requestId, innerException),
            429 => new PoliPageRateLimitException(
                code, status, message, requestId, RetryAfterParser.Parse(headers, DateTimeOffset.UtcNow), innerException),
            _ => new PoliPageException(code, status, message, requestId, innerException),
        };
    }

    private static bool LooksLikeJson(string s)
    {
        var trimmed = s.AsSpan().TrimStart();
        return trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[');
    }
}
