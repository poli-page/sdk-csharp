namespace PoliPage;

/// <summary>
/// Canonical wire payload for framework integrations that surface a
/// <see cref="PoliPageException"/> as a JSON response. Sourced via
/// <see cref="PoliPageException.ToPayload"/>; integrations write the
/// fields verbatim to the HTTP body (or extract them into RFC 7807
/// ProblemDetails extensions).
/// </summary>
/// <param name="Code">The wire-level error code from the API.</param>
/// <param name="Message">The human-readable error message.</param>
/// <param name="Status">
/// The HTTP status to surface on the wire. The API status for
/// <see cref="PoliPageException.StatusCode"/>-bearing failures, 503 for
/// network failures, 504 for timeouts, or <see langword="null"/> for the
/// bare base class.
/// </param>
/// <param name="RequestId">The server-assigned request identifier, or <see langword="null"/>.</param>
public sealed record PoliPageErrorPayload(
    string Code,
    string Message,
    int? Status,
    string? RequestId);
