namespace PoliPage;

/// <summary>
/// Thrown when the API responds with HTTP 400 or 422.
/// Indicates that one or more request parameters failed server-side validation.
/// Inspect <see cref="Exception.Message"/> for details on which fields are invalid.
/// </summary>
public sealed class PoliPageValidationException : PoliPageException
{
    /// <summary>Initialises a new instance of <see cref="PoliPageValidationException"/> with default values.</summary>
    public PoliPageValidationException() : this(PoliPageErrorCode.ValidationError, 422, "Validation failed.") { }

    /// <summary>Initialises a new instance of <see cref="PoliPageValidationException"/> with the specified message.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    public PoliPageValidationException(string message) : this(PoliPageErrorCode.ValidationError, 422, message) { }

    /// <summary>Initialises a new instance of <see cref="PoliPageValidationException"/> with the specified message and inner exception.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PoliPageValidationException(string message, Exception innerException)
        : this(PoliPageErrorCode.ValidationError, 422, message, innerException: innerException) { }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageValidationException"/>.
    /// </summary>
    /// <param name="code">The wire-level error code (see <see cref="PoliPageErrorCode"/>).</param>
    /// <param name="statusCode">The HTTP status code (400 or 422).</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="requestId">The server-assigned request identifier, if available.</param>
    /// <param name="innerException">The underlying exception that caused this error, if any.</param>
    public PoliPageValidationException(
        string code,
        int statusCode,
        string message,
        string? requestId = null,
        Exception? innerException = null)
        : base(code, statusCode, message, requestId, innerException)
    {
    }
}
