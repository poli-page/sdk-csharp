using System.Globalization;

namespace PoliPage.Internal;

/// <summary>
/// The errors the SDK raises itself, with their exact messages. The messages are part of
/// the Poli Page API contract: every Poli Page SDK uses the same texts.
/// The underlying exception, when there is one, is kept as
/// <see cref="Exception.InnerException"/>.
/// </summary>
internal static class SdkErrors
{
    internal const string InvalidOptionsMessage = "apiKey is required";
    internal const string NetworkErrorMessage = "Network request failed";
    internal const string AbortedMessage = "Request was aborted";
    internal const string DownloadNetworkMessage = "Failed to download PDF: network error";
    internal const string DownloadTimeoutMessage = "Failed to download PDF: timeout";

    // Codes that are never retried although their status is (a 429 or a 5xx): the quota
    // codes last until the monthly reset and STORAGE_ACCESS_DENIED until the customer
    // restores its bucket role, so waiting and trying again cannot help.
    private static readonly HashSet<string> NonRetryableCodes = new(StringComparer.Ordinal)
    {
        PoliPageErrorCode.QuotaExceeded,
        PoliPageErrorCode.OverageCapExceeded,
        PoliPageErrorCode.StorageAccessDenied,
    };

    internal static bool IsNonRetryableCode(string code) => NonRetryableCodes.Contains(code);

    internal static PoliPageNetworkException NetworkError(Exception inner)
        => new(PoliPageErrorCode.NetworkError, NetworkErrorMessage, inner);

    /// <summary>
    /// A timed-out attempt. <paramref name="bound"/> is the timeout that fired, so the
    /// message states the real bound.
    /// </summary>
    internal static PoliPageException Timeout(TimeSpan bound, Exception? inner)
        => new(
            PoliPageErrorCode.Timeout,
            statusCode: 0,
            string.Create(CultureInfo.InvariantCulture, $"Request timed out after {(long)bound.TotalMilliseconds}ms"),
            requestId: null,
            inner);

    internal static PoliPageException Aborted(Exception? inner)
        => new(PoliPageErrorCode.Aborted, statusCode: 0, AbortedMessage, requestId: null, inner);

    internal static PoliPageDownloadException DownloadNetwork(Exception inner)
        => new(PoliPageErrorCode.DownloadFailed, statusCode: 0, DownloadNetworkMessage, requestId: null, inner);

    /// <summary>A presigned-URL download that outlasted its timeout: no status.</summary>
    internal static PoliPageDownloadException DownloadTimeout(Exception? inner)
        => new(PoliPageErrorCode.DownloadFailed, statusCode: 0, DownloadTimeoutMessage, requestId: null, inner);

    /// <summary>
    /// A 2xx API answer whose body is not JSON, from a call that expects JSON. It keeps the
    /// real status and the answer's request id; <paramref name="inner"/> is the parser's error.
    /// </summary>
    internal static PoliPageInvalidResponseException InvalidResponse(int status, string? requestId, Exception inner)
        => new(
            PoliPageErrorCode.InvalidResponse,
            status,
            string.Create(CultureInfo.InvariantCulture, $"Invalid response: HTTP {status} body is not JSON"),
            requestId,
            inner);

    /// <summary>
    /// <see cref="PoliPageClient.RenderToFileAsync"/> could not write the file: the message
    /// carries the file system's own text, and <paramref name="inner"/> is that error.
    /// </summary>
    internal static PoliPageException IoError(Exception inner)
        => new(PoliPageErrorCode.IoError, statusCode: 0, "Failed to write PDF: " + inner.Message, requestId: null, inner);

    /// <summary>
    /// The file system failures of <see cref="PoliPageClient.RenderToFileAsync"/>: an I/O
    /// error (a missing directory, a full disk, a file in use), a denied access, or a path the
    /// platform does not support.
    /// </summary>
    internal static bool IsFileSystemFailure(Exception exception)
        => exception is IOException or UnauthorizedAccessException or NotSupportedException;

    internal static PoliPageDownloadException DownloadStatus(int status, string? requestId)
        => new(
            PoliPageErrorCode.DownloadFailed,
            status,
            string.Create(CultureInfo.InvariantCulture, $"Failed to download PDF: HTTP {status}"),
            requestId);

    /// <summary>
    /// The failures an HTTP send or a body read can raise when no complete answer came
    /// back: a connection, DNS or TLS failure, a connection cut mid-body, or a cancellation
    /// (the caller's token, an SDK timeout, or the HttpClient's own timeout). An
    /// <see cref="ObjectDisposedException"/> (a disposed HttpClient) is a programming error,
    /// not a network failure, and propagates unchanged.
    /// </summary>
    internal static bool IsTransportFailure(Exception exception)
        => exception is HttpRequestException or IOException or OperationCanceledException;
}
