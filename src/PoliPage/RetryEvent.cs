using System.Net;

namespace PoliPage;

/// <summary>
/// Carries context about a single retry attempt so that callers can observe
/// or log retry activity via <see cref="PoliPageClientOptions.OnRetry"/>.
/// </summary>
/// <param name="Attempt">
/// The one-based index of the attempt that is about to be made.
/// For example, <c>1</c> means the first retry (after the initial failure).
/// </param>
/// <param name="Delay">The delay the client will wait before this attempt.</param>
/// <param name="StatusCode">
/// The HTTP status code from the previous response, if one was received.
/// <see langword="null"/> when the failure was a network-level error (e.g.
/// timeout or <see cref="System.Net.Http.HttpRequestException"/>).
/// </param>
/// <param name="Reason">A human-readable explanation of why the request is being retried.</param>
public sealed record RetryEvent(
    int Attempt,
    TimeSpan Delay,
    HttpStatusCode? StatusCode,
    string Reason);
