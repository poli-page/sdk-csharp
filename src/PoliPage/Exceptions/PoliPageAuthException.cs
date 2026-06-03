namespace PoliPage;

/// <summary>
/// Thrown when the API responds with HTTP 401 or 403.
/// Indicates that the API key is missing, invalid, or lacks permission for the requested action.
/// Check <see cref="PoliPageException.Code"/> to distinguish
/// <see cref="PoliPageErrorCode.MissingApiKey"/>, <see cref="PoliPageErrorCode.InvalidApiKey"/>,
/// and <see cref="PoliPageErrorCode.Forbidden"/>.
/// </summary>
public sealed class PoliPageAuthException : PoliPageException
{
    /// <summary>Initialises a new instance of <see cref="PoliPageAuthException"/> with default values.</summary>
    public PoliPageAuthException() : this(PoliPageErrorCode.InvalidApiKey, 401, "Authentication failed.") { }

    /// <summary>Initialises a new instance of <see cref="PoliPageAuthException"/> with the specified message.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    public PoliPageAuthException(string message) : this(PoliPageErrorCode.InvalidApiKey, 401, message) { }

    /// <summary>Initialises a new instance of <see cref="PoliPageAuthException"/> with the specified message and inner exception.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PoliPageAuthException(string message, Exception innerException)
        : this(PoliPageErrorCode.InvalidApiKey, 401, message, innerException: innerException) { }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageAuthException"/>.
    /// </summary>
    /// <param name="code">The wire-level error code (see <see cref="PoliPageErrorCode"/>).</param>
    /// <param name="statusCode">The HTTP status code (401 or 403).</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="requestId">The server-assigned request identifier, if available.</param>
    /// <param name="innerException">The underlying exception that caused this error, if any.</param>
    public PoliPageAuthException(
        string code,
        int statusCode,
        string message,
        string? requestId = null,
        Exception? innerException = null)
        : base(code, statusCode, message, requestId, innerException)
    {
    }
}
