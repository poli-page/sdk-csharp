namespace PoliPage;

/// <summary>
/// Thrown (code <c>invalid_response</c>) when the API answers a 2xx whose body is not JSON to
/// a call that expects JSON: the API call of <see cref="Render.PdfAsync"/>,
/// <see cref="Render.PdfStreamAsync"/> and <see cref="Render.DocumentAsync"/>,
/// <see cref="Documents.GetAsync"/> and <see cref="Documents.ThumbnailsAsync"/>. Such a body
/// usually comes from a proxy or a captive portal between the SDK and the API, which answers
/// the same way again, so it is never retried.
/// </summary>
/// <remarks>
/// <see cref="PoliPageException.StatusCode"/> is the real 2xx status and
/// <see cref="PoliPageException.RequestId"/> the answer's <c>X-Request-Id</c>, when present.
/// <see cref="PoliPageException.ToPayload"/> answers <c>502</c> instead, since an integration
/// must not answer an error with a 2xx. The JSON parser's error is the
/// <see cref="Exception.InnerException"/>.
/// </remarks>
public sealed class PoliPageInvalidResponseException : PoliPageException
{
    /// <summary>Initialises a new instance of <see cref="PoliPageInvalidResponseException"/> with default values.</summary>
    public PoliPageInvalidResponseException()
        : this(PoliPageErrorCode.InvalidResponse, 0, "Invalid response: body is not JSON")
    {
    }

    /// <summary>Initialises a new instance of <see cref="PoliPageInvalidResponseException"/> with the specified message.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    public PoliPageInvalidResponseException(string message)
        : this(PoliPageErrorCode.InvalidResponse, 0, message)
    {
    }

    /// <summary>Initialises a new instance of <see cref="PoliPageInvalidResponseException"/> with the specified message and inner exception.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PoliPageInvalidResponseException(string message, Exception innerException)
        : this(PoliPageErrorCode.InvalidResponse, 0, message, innerException: innerException)
    {
    }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageInvalidResponseException"/>.
    /// </summary>
    /// <param name="code">The error code (see <see cref="PoliPageErrorCode.InvalidResponse"/>).</param>
    /// <param name="statusCode">The 2xx status of the API's answer.</param>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="requestId">The answer's <c>X-Request-Id</c>, if present.</param>
    /// <param name="innerException">The JSON parser's error, if any.</param>
    public PoliPageInvalidResponseException(
        string code,
        int statusCode,
        string message,
        string? requestId = null,
        Exception? innerException = null)
        : base(code, statusCode, message, requestId, innerException)
    {
    }
}
