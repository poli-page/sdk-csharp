using System.Text.Json.Serialization;

namespace PoliPage;

/// <summary>
/// Canonical wire payload for framework integrations that surface a
/// <see cref="PoliPageException"/> as a JSON response. Sourced via
/// <see cref="PoliPageException.ToPayload"/>; integrations write the
/// fields verbatim to the HTTP body (or extract them into RFC 7807
/// ProblemDetails extensions). <c>System.Text.Json</c> serializes it with exactly
/// the keys <c>code</c>, <c>message</c>, <c>status</c> and <c>requestId</c>, the
/// same in every Poli Page SDK, whatever the serializer's naming policy;
/// <c>status</c> and <c>requestId</c> are <c>null</c> when unknown.
/// </summary>
/// <param name="Code">The wire-level error code from the API.</param>
/// <param name="Message">The human-readable error message.</param>
/// <param name="Status">
/// The HTTP status to surface on the wire. The API status for
/// <see cref="PoliPageException.StatusCode"/>-bearing failures, 503 for
/// network failures, 504 for timeouts, 502 for <c>invalid_response</c>, or
/// <see langword="null"/> for the bare base class.
/// </param>
/// <param name="RequestId">The server-assigned request identifier, or <see langword="null"/>.</param>
public sealed record PoliPageErrorPayload(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("status")] int? Status,
    [property: JsonPropertyName("requestId")] string? RequestId);
