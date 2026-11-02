namespace PoliPage.Internal;

/// <summary>The API calls of one client, with its retries, timeouts and hooks.</summary>
/// <remarks>
/// Each method returns the 2xx answer with its whole body already read into memory, within
/// the attempt and its timeout; callers parse it without further I/O. A call that expects
/// JSON (every POST, and a GET with <c>expectsJson</c>) gets a body that is valid JSON: a 2xx
/// whose body is not throws <c>invalid_response</c>, never retried. Every other outcome
/// throws a <see cref="PoliPageException"/>.
/// </remarks>
internal interface ITransport
{
    /// <summary>
    /// Sends a POST request and returns the 2xx response, whose body is JSON (else
    /// <c>invalid_response</c>).
    /// </summary>
    Task<HttpResponseMessage> PostAsync(
        string path,
        object body,
        string idempotencyKey,
        RequestOptions? options,
        string accept,
        CancellationToken cancellationToken);

    /// <summary>
    /// Sends a GET request and returns the 2xx response; with <paramref name="expectsJson"/>,
    /// a 2xx whose body is not JSON throws <c>invalid_response</c>.
    /// </summary>
    Task<HttpResponseMessage> GetAsync(
        string path,
        RequestOptions? options,
        string accept,
        bool expectsJson,
        CancellationToken cancellationToken);

    /// <summary>Sends a DELETE request and returns the 2xx response.</summary>
    Task<HttpResponseMessage> DeleteAsync(
        string path,
        RequestOptions? options,
        CancellationToken cancellationToken);
}
