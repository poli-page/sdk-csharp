namespace PoliPage;

/// <summary>
/// Thrown when the API responds with HTTP 402.
/// Indicates that the account has an outstanding balance that must be settled before
/// further API calls are allowed.
/// </summary>
public sealed class PoliPagePaymentRequiredException : PoliPageException
{
    /// <summary>Initialises a new instance of <see cref="PoliPagePaymentRequiredException"/> with default values.</summary>
    public PoliPagePaymentRequiredException() : this(PoliPageErrorCode.PaymentRequired, 402, "Payment required.") { }

    /// <summary>Initialises a new instance of <see cref="PoliPagePaymentRequiredException"/> with the specified message.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    public PoliPagePaymentRequiredException(string message) : this(PoliPageErrorCode.PaymentRequired, 402, message) { }

    /// <summary>Initialises a new instance of <see cref="PoliPagePaymentRequiredException"/> with the specified message and inner exception.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PoliPagePaymentRequiredException(string message, Exception innerException)
        : this(PoliPageErrorCode.PaymentRequired, 402, message, innerException: innerException) { }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPagePaymentRequiredException"/>.
    /// </summary>
    /// <param name="code">The wire-level error code (see <see cref="PoliPageErrorCode"/>).</param>
    /// <param name="statusCode">The HTTP status code (402).</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="requestId">The server-assigned request identifier, if available.</param>
    /// <param name="innerException">The underlying exception that caused this error, if any.</param>
    public PoliPagePaymentRequiredException(
        string code,
        int statusCode,
        string message,
        string? requestId = null,
        Exception? innerException = null)
        : base(code, statusCode, message, requestId, innerException)
    {
    }
}
