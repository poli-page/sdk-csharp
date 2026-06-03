namespace PoliPage;

/// <summary>
/// Carries context about a successful HTTP response so callers can observe or
/// instrument response behaviour via <see cref="PoliPageClientOptions.OnResponse"/>.
/// Fires only for 2xx responses; failed responses surface through
/// <see cref="PoliPageClientOptions.OnRetry"/> or <see cref="PoliPageClientOptions.OnError"/>.
/// </summary>
/// <param name="Status">HTTP status code of the successful response.</param>
/// <param name="RequestId">
/// The server's <c>X-Request-Id</c> header value, when present. <see langword="null"/> when absent.
/// </param>
/// <param name="DurationMs">Wall-clock latency of the HTTP round-trip in milliseconds.</param>
public sealed record ResponseEvent(int Status, string? RequestId, long DurationMs);
