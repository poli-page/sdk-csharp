using System.Net;

namespace PoliPage;

/// <summary>
/// Carries context about a single retry attempt so that callers can observe
/// or log retry activity via <see cref="PoliPageClientOptions.OnRetry"/>.
/// </summary>
/// <param name="Attempt">
/// The one-based number of the attempt that is about to be made: <c>2</c> for the first
/// retry (the initial send is attempt <c>1</c>), as <see cref="RequestEvent.Attempt"/>
/// numbers it.
/// </param>
/// <param name="Delay">
/// The delay the client will wait before this attempt: the <c>Retry-After</c> header's
/// value (clamped to 0–30 s, without jitter) when the failed answer had one, otherwise the
/// exponential backoff.
/// </param>
/// <param name="StatusCode">
/// The HTTP status code from the previous response, if one was received.
/// <see langword="null"/> when the failure was a network-level error (e.g.
/// timeout or <see cref="System.Net.Http.HttpRequestException"/>).
/// </param>
/// <param name="Reason">The message of the error that caused the retry.</param>
public sealed record RetryEvent(
    int Attempt,
    TimeSpan Delay,
    HttpStatusCode? StatusCode,
    string Reason);
