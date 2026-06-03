// Why: RCS1194 requires propagating all base-class constructors. PoliPageRateLimitException
// introduces RetryAfter which cannot be set via the inherited (code, statusCode, message, requestId?,
// innerException?) signature, so a strict propagation is not possible. The three standard
// Exception constructors ((), (string), (string, Exception)) are present; the full Poli Page
// constructor is exposed with RetryAfter added between requestId and innerException.
#pragma warning disable RCS1194
namespace PoliPage;

/// <summary>
/// Thrown when the API responds with HTTP 429.
/// Indicates that the caller has exceeded the allowed request rate.
/// Wait for <see cref="RetryAfter"/> before making additional requests.
/// </summary>
public sealed class PoliPageRateLimitException : PoliPageException
{
    /// <summary>
    /// The recommended wait time before retrying, parsed from the <c>Retry-After</c>
    /// response header. Capped at 30 seconds per the SDK specification.
    /// <see langword="null"/> when the server did not include a <c>Retry-After</c> header.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>Initialises a new instance of <see cref="PoliPageRateLimitException"/> with default values.</summary>
    public PoliPageRateLimitException()
        : base(PoliPageErrorCode.QuotaExceeded, 429, "Rate limit exceeded.")
    {
    }

    /// <summary>Initialises a new instance of <see cref="PoliPageRateLimitException"/> with the specified message.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    public PoliPageRateLimitException(string message)
        : base(PoliPageErrorCode.QuotaExceeded, 429, message)
    {
    }

    /// <summary>Initialises a new instance of <see cref="PoliPageRateLimitException"/> with the specified message and inner exception.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PoliPageRateLimitException(string message, Exception innerException)
        : base(PoliPageErrorCode.QuotaExceeded, 429, message, innerException: innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageRateLimitException"/>.
    /// </summary>
    /// <param name="code">The wire-level error code (see <see cref="PoliPageErrorCode"/>).</param>
    /// <param name="statusCode">The HTTP status code (429).</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="requestId">The server-assigned request identifier, if available.</param>
    /// <param name="retryAfter">
    /// The recommended wait duration before retrying, or <see langword="null"/> if the
    /// server did not indicate a delay.
    /// </param>
    /// <param name="innerException">The underlying exception that caused this error, if any.</param>
    public PoliPageRateLimitException(
        string code,
        int statusCode,
        string message,
        string? requestId = null,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(code, statusCode, message, requestId, innerException)
    {
        RetryAfter = retryAfter;
    }
}
#pragma warning restore RCS1194
