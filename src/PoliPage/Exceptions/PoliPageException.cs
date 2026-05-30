namespace PoliPage;

/// <summary>
/// Base class for all exceptions thrown by the Poli Page SDK.
/// Catch this type to handle any SDK error; catch a derived type for
/// specific error categories (auth, rate-limit, validation, etc.).
/// </summary>
public class PoliPageException : Exception
{
    /// <summary>
    /// The wire-level error code returned by the API (e.g. <c>"UNAUTHORIZED"</c>).
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
}
