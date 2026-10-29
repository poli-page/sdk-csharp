using System.Globalization;

namespace PoliPage.Internal;

/// <summary>
/// The errors the SDK raises itself, with the exact messages of the SDK specification
/// (§8.3). The messages are part of the API contract: every SDK uses the same texts.
/// The underlying exception, when there is one, is kept as
/// <see cref="Exception.InnerException"/>.
/// </summary>
internal static class SdkErrors
{
    internal const string InvalidOptionsMessage = "apiKey is required";
    internal const string NetworkErrorMessage = "Network request failed";
    internal const string AbortedMessage = "Request was aborted";
    internal const string DownloadNetworkMessage = "Failed to download PDF: network error";

    // Codes that are never retried although their status is (a 429 or a 5xx): the quota
    // codes last until the monthly reset and STORAGE_ACCESS_DENIED until the customer
    // restores its bucket role, so waiting and trying again cannot help (spec §9.1).
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

    internal static PoliPageDownloadException DownloadStatus(int status, string? requestId)
        => new(
            PoliPageErrorCode.DownloadFailed,
            status,
            string.Create(CultureInfo.InvariantCulture, $"Failed to download PDF: HTTP {status}"),
            requestId);

    /// <summary>
    /// The failures an HTTP send or a body read can raise when no complete answer came
    /// back: a connection, DNS or TLS failure, a connection cut mid-body, or a cancellation
    /// (the caller's token, an SDK timeout, or the HttpClient's own timeout).
    /// </summary>
    internal static bool IsTransportFailure(Exception exception)
        => exception is HttpRequestException or IOException or OperationCanceledException or ObjectDisposedException;
}
