namespace PoliPage;

/// <summary>
/// Thrown when the API responds with HTTP 404.
/// Indicates that the requested resource (project, template, document, or version) does not exist.
/// Check <see cref="PoliPageException.Code"/> for <see cref="PoliPageErrorCode.NotFound"/>,
/// <see cref="PoliPageErrorCode.VersionNotFound"/>, or <see cref="PoliPageErrorCode.DocumentNotFound"/>
/// to identify which resource was missing.
/// </summary>
public sealed class PoliPageNotFoundException : PoliPageException
{
    /// <summary>Initialises a new instance of <see cref="PoliPageNotFoundException"/> with default values.</summary>
    public PoliPageNotFoundException() : this(PoliPageErrorCode.NotFound, 404, "Resource not found.") { }

    /// <summary>Initialises a new instance of <see cref="PoliPageNotFoundException"/> with the specified message.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    public PoliPageNotFoundException(string message) : this(PoliPageErrorCode.NotFound, 404, message) { }

    /// <summary>Initialises a new instance of <see cref="PoliPageNotFoundException"/> with the specified message and inner exception.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PoliPageNotFoundException(string message, Exception innerException)
        : this(PoliPageErrorCode.NotFound, 404, message, innerException: innerException) { }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageNotFoundException"/>.
    /// </summary>
    /// <param name="code">The wire-level error code (see <see cref="PoliPageErrorCode"/>).</param>
    /// <param name="statusCode">The HTTP status code (404).</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="requestId">The server-assigned request identifier, if available.</param>
    /// <param name="innerException">The underlying exception that caused this error, if any.</param>
    public PoliPageNotFoundException(
        string code,
        int statusCode,
        string message,
        string? requestId = null,
        Exception? innerException = null)
        : base(code, statusCode, message, requestId, innerException)
    {
    }
}
