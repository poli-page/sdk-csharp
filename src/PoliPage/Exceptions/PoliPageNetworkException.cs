// Why: RCS1194 requires the standard Exception constructor set. PoliPageNetworkException
// intentionally omits the (code, statusCode, message, requestId?, innerException?) base-class
// shape because network exceptions always have StatusCode=0 and no requestId — exposing those
// parameters would mislead callers. The three standard Exception constructors are present.
#pragma warning disable RCS1194
namespace PoliPage;

/// <summary>
/// Thrown when a DNS resolution failure, TCP connection refusal, TLS handshake error,
/// or transport-level timeout prevents the request from reaching the server.
/// <see cref="PoliPageException.StatusCode"/> is always <c>0</c> because no HTTP response
/// was received.
/// Inspect <see cref="Exception.InnerException"/> for the underlying
/// <see cref="System.Net.Http.HttpRequestException"/> or <see cref="TaskCanceledException"/>.
/// </summary>
public sealed class PoliPageNetworkException : PoliPageException
{
    /// <summary>Initialises a new instance of <see cref="PoliPageNetworkException"/> with default values.</summary>
    public PoliPageNetworkException()
        : base(PoliPageErrorCode.Network, statusCode: 0, "A network error occurred.")
    {
    }

    /// <summary>Initialises a new instance of <see cref="PoliPageNetworkException"/> with the specified message.</summary>
    /// <param name="message">A human-readable description of the network failure.</param>
    public PoliPageNetworkException(string message)
        : base(PoliPageErrorCode.Network, statusCode: 0, message)
    {
    }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageNetworkException"/> with the specified message and inner exception.
    /// Uses <see cref="PoliPageErrorCode.Network"/> as the error code.
    /// </summary>
    /// <param name="message">A human-readable description of the network failure.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PoliPageNetworkException(string message, Exception innerException)
        : base(PoliPageErrorCode.Network, statusCode: 0, message, requestId: null, innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageNetworkException"/> with an explicit error code.
    /// </summary>
    /// <param name="code">The wire-level error code (see <see cref="PoliPageErrorCode"/>).</param>
    /// <param name="message">A human-readable description of the network failure.</param>
    /// <param name="innerException">
    /// The underlying <see cref="System.Net.Http.HttpRequestException"/> or
    /// <see cref="TaskCanceledException"/> that caused this error.
    /// </param>
    public PoliPageNetworkException(string code, string message, Exception innerException)
        : base(code, statusCode: 0, message, requestId: null, innerException)
    {
    }
}
#pragma warning restore RCS1194
