// Why: RCS1194 requires the standard Exception constructor set. PoliPageNetworkException
// intentionally omits the (code, statusCode, message, requestId?, innerException?) base-class
// shape because network exceptions always have StatusCode=0 and no requestId — exposing those
// parameters would mislead callers. The three standard Exception constructors are present.
#pragma warning disable RCS1194
namespace PoliPage;

/// <summary>
/// Thrown for a <c>network_error</c>: a DNS resolution failure, TCP connection refusal or
/// TLS handshake error, or a connection that closed before the whole answer arrived.
/// <see cref="PoliPageException.StatusCode"/> is always <c>0</c> because no complete HTTP
/// response was received. A timed-out attempt is not this type: it is a
/// <see cref="PoliPageException"/> with <c>Code == timeout</c>.
/// Inspect <see cref="Exception.InnerException"/> for the underlying exception, typically
/// an <see cref="System.Net.Http.HttpRequestException"/> or an <see cref="IOException"/>.
/// </summary>
public sealed class PoliPageNetworkException : PoliPageException
{
    /// <inheritdoc/>
    protected override int? PayloadStatus() => 503;

    /// <summary>Initialises a new instance of <see cref="PoliPageNetworkException"/> with default values.</summary>
    public PoliPageNetworkException()
        : base(PoliPageErrorCode.NetworkError, statusCode: 0, "A network error occurred.")
    {
    }

    /// <summary>Initialises a new instance of <see cref="PoliPageNetworkException"/> with the specified message.</summary>
    /// <param name="message">A human-readable description of the network failure.</param>
    public PoliPageNetworkException(string message)
        : base(PoliPageErrorCode.NetworkError, statusCode: 0, message)
    {
    }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageNetworkException"/> with the specified message and inner exception.
    /// Uses <see cref="PoliPageErrorCode.NetworkError"/> as the error code.
    /// </summary>
    /// <param name="message">A human-readable description of the network failure.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PoliPageNetworkException(string message, Exception innerException)
        : base(PoliPageErrorCode.NetworkError, statusCode: 0, message, requestId: null, innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageNetworkException"/> with an explicit error code.
    /// </summary>
    /// <param name="code">The wire-level error code (see <see cref="PoliPageErrorCode"/>).</param>
    /// <param name="message">A human-readable description of the network failure.</param>
    /// <param name="innerException">
    /// The underlying exception that caused this error, typically an
    /// <see cref="System.Net.Http.HttpRequestException"/> or an <see cref="IOException"/>.
    /// </param>
    public PoliPageNetworkException(string code, string message, Exception innerException)
        : base(code, statusCode: 0, message, requestId: null, innerException)
    {
    }
}
#pragma warning restore RCS1194
