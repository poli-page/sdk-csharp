namespace PoliPage;

/// <summary>
/// Thrown when the API responds with HTTP 410.
/// Indicates that the requested resource previously existed but has been permanently removed.
/// Unlike <see cref="PoliPageNotFoundException"/>, this is not retryable — the resource will
/// not come back.
/// </summary>
public sealed class PoliPageGoneException : PoliPageException
{
    /// <summary>Initialises a new instance of <see cref="PoliPageGoneException"/> with default values.</summary>
    public PoliPageGoneException() : this(PoliPageErrorCode.Gone, 410, "Resource is permanently gone.") { }

    /// <summary>Initialises a new instance of <see cref="PoliPageGoneException"/> with the specified message.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    public PoliPageGoneException(string message) : this(PoliPageErrorCode.Gone, 410, message) { }

    /// <summary>Initialises a new instance of <see cref="PoliPageGoneException"/> with the specified message and inner exception.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PoliPageGoneException(string message, Exception innerException)
        : this(PoliPageErrorCode.Gone, 410, message, innerException: innerException) { }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageGoneException"/>.
    /// </summary>
    /// <param name="code">The wire-level error code (see <see cref="PoliPageErrorCode"/>).</param>
    /// <param name="statusCode">The HTTP status code (410).</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="requestId">The server-assigned request identifier, if available.</param>
    /// <param name="innerException">The underlying exception that caused this error, if any.</param>
    public PoliPageGoneException(
        string code,
        int statusCode,
        string message,
        string? requestId = null,
        Exception? innerException = null)
        : base(code, statusCode, message, requestId, innerException)
    {
    }
}
