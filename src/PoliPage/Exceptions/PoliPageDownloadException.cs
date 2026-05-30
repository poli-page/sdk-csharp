namespace PoliPage;

/// <summary>
/// Thrown when a presigned URL download from remote storage (e.g. S3) fails.
/// <see cref="PoliPageException.StatusCode"/> reflects the HTTP status returned by the
/// storage service — it is distinct from the Poli Page API status code.
/// </summary>
public sealed class PoliPageDownloadException : PoliPageException
{
    /// <summary>Initialises a new instance of <see cref="PoliPageDownloadException"/> with default values.</summary>
    public PoliPageDownloadException() : this(PoliPageErrorCode.DownloadFailed, 0, "Download failed.") { }

    /// <summary>Initialises a new instance of <see cref="PoliPageDownloadException"/> with the specified message.</summary>
    /// <param name="message">A human-readable description of the download failure.</param>
    public PoliPageDownloadException(string message) : this(PoliPageErrorCode.DownloadFailed, 0, message) { }

    /// <summary>Initialises a new instance of <see cref="PoliPageDownloadException"/> with the specified message and inner exception.</summary>
    /// <param name="message">A human-readable description of the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public PoliPageDownloadException(string message, Exception innerException)
        : this(PoliPageErrorCode.DownloadFailed, 0, message, innerException: innerException) { }

    /// <summary>
    /// Initialises a new instance of <see cref="PoliPageDownloadException"/>.
    /// </summary>
    /// <param name="code">The wire-level error code (see <see cref="PoliPageErrorCode"/>).</param>
    /// <param name="statusCode">The HTTP status code returned by the storage service.</param>
    /// <param name="message">A human-readable description of the download failure.</param>
    /// <param name="requestId">The server-assigned request identifier, if available.</param>
    /// <param name="innerException">The underlying exception that caused this error, if any.</param>
    public PoliPageDownloadException(
        string code,
        int statusCode,
        string message,
        string? requestId = null,
        Exception? innerException = null)
        : base(code, statusCode, message, requestId, innerException)
    {
    }
}
