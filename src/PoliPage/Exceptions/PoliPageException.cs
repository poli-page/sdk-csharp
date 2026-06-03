namespace PoliPage;

/// <summary>
/// Base class for all exceptions thrown by the Poli Page SDK.
/// Catch this type to handle any SDK error; catch a derived type for
/// specific error categories (auth, rate-limit, validation, etc.).
/// </summary>
public class PoliPageException : Exception
{
    /// <summary>
    /// The wire-level error code returned by the API (e.g. <c>"INVALID_API_KEY"</c>).
    /// Use <see cref="PoliPageErrorCode"/> constants to compare without magic strings.
    /// </summary>
    public string Code { get; }

    /// <summary>
    /// The HTTP status code of the response that triggered this exception,
    /// or <c>0</c> for network-level failures where no HTTP response was received.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// The opaque request identifier returned by the server in the JSON envelope
    /// or the <c>X-Request-Id</c> response header.
    /// Include this in support requests to help Poli Page engineers trace the call.
    /// May be <see langword="null"/> when the error occurred before the request reached the server.
    /// </summary>
    public string? RequestId { get; }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageException"/> with default values.
    /// </summary>
    public PoliPageException()
        : this(PoliPageErrorCode.Unknown, 0, "An unexpected SDK error occurred.")
    {
    }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageException"/> with the specified message.
    /// </summary>
    /// <param name="message">A human-readable description of the error.</param>
    public PoliPageException(string message)
        : this(PoliPageErrorCode.Unknown, 0, message)
    {
    }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageException"/> with the specified message and inner exception.
    /// </summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PoliPageException(string message, Exception innerException)
        : this(PoliPageErrorCode.Unknown, 0, message, innerException: innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageException"/>.
    /// </summary>
    /// <param name="code">The wire-level error code (see <see cref="PoliPageErrorCode"/>).</param>
    /// <param name="statusCode">The HTTP status code, or <c>0</c> for network failures.</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="requestId">The server-assigned request identifier, if available.</param>
    /// <param name="innerException">The underlying exception that caused this error, if any.</param>
    public PoliPageException(
        string code,
        int statusCode,
        string message,
        string? requestId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        RequestId = requestId;
    }

    /// <summary>
    /// <see langword="true"/> for HTTP 401 / 403 — missing, invalid, or unauthorized API key.
    /// </summary>
    /// <remarks>Mirrors <c>sdk-node/src/error.ts:84-86</c>.</remarks>
    public bool IsAuthError()
        => StatusCode == 401 || StatusCode == 403;

    /// <summary>
    /// <see langword="true"/> for HTTP 429 — the request was rate-limited.
    /// The SDK has already retried up to <see cref="PoliPageClientOptions.MaxRetries"/>
    /// times before surfacing this; back off further at the caller level if you see it.
    /// </summary>
    /// <remarks>Mirrors <c>sdk-node/src/error.ts:100-102</c>.</remarks>
    public bool IsRateLimitError()
        => StatusCode == 429;

    /// <summary>
    /// <see langword="true"/> for HTTP 400 — request payload failed validation.
    /// </summary>
    /// <remarks>Mirrors <c>sdk-node/src/error.ts:115-117</c>.</remarks>
    public bool IsValidationError()
        => StatusCode == 400;

    /// <summary>
    /// <see langword="true"/> for transport-level failures: DNS errors, connection refused,
    /// TLS failures (<c>code: network_error</c>) or per-request timeouts (<c>code: timeout</c>).
    /// </summary>
    /// <remarks>Mirrors <c>sdk-node/src/error.ts:131-133</c>.</remarks>
    public bool IsNetworkError()
        => string.Equals(Code, PoliPageErrorCode.NetworkError, StringComparison.Ordinal)
        || string.Equals(Code, PoliPageErrorCode.Timeout, StringComparison.Ordinal);

    /// <summary>
    /// <see langword="true"/> if the SDK considers this error retryable (5xx, 429, network,
    /// timeout). Caller-aborted requests (<c>code: aborted</c>) are NEVER retryable.
    /// </summary>
    /// <remarks>Mirrors <c>sdk-node/src/error.ts:149-155</c>.</remarks>
    public bool IsRetryable()
    {
        if (string.Equals(Code, PoliPageErrorCode.Aborted, StringComparison.Ordinal))
            return false;
        if (IsNetworkError())
            return true;
        if (StatusCode >= 500)
            return true;
        if (StatusCode == 429)
            return true;
        return false;
    }

    /// <summary>
    /// Returns the canonical wire payload for framework integrations:
    /// <c>{ Code, Message, Status, RequestId }</c>. The
    /// <see cref="StatusCode"/> property is unchanged for transport
    /// failures — only the payload surfaces 503/504, so callers that
    /// inspect <see cref="StatusCode"/> directly are not affected.
    /// </summary>
    public PoliPageErrorPayload ToPayload()
        => new(Code, Message, PayloadStatus(), RequestId);

    /// <summary>
    /// Resolves the wire status surfaced by <see cref="ToPayload"/>.
    /// Default: the API status when present, 504 for the bare-base
    /// timeout shape, otherwise <see langword="null"/>. Subclasses override
    /// for transport-specific defaults (network → 503).
    /// </summary>
    protected virtual int? PayloadStatus()
    {
        if (StatusCode != 0)
            return StatusCode;
        return string.Equals(Code, PoliPageErrorCode.Timeout, StringComparison.Ordinal) ? 504 : null;
    }
}
