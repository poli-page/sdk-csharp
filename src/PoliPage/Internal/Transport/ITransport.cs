namespace PoliPage.Internal;

/// <summary>The API calls of one client, with its retries, timeouts and hooks.</summary>
/// <remarks>
/// Each method returns the 2xx answer with its whole body already read into memory, within
/// the attempt and its timeout; callers parse it without further I/O. Every other outcome
/// throws a <see cref="PoliPageException"/>.
/// </remarks>
internal interface ITransport
{
    /// <summary>Sends a POST request and returns the 2xx response.</summary>
    Task<HttpResponseMessage> PostAsync(
        string path,
        object body,
        string idempotencyKey,
        RequestOptions? options,
        string accept,
        CancellationToken cancellationToken);

    /// <summary>Sends a GET request and returns the 2xx response.</summary>
    Task<HttpResponseMessage> GetAsync(
        string path,
        RequestOptions? options,
        string accept,
        CancellationToken cancellationToken);

    /// <summary>Sends a DELETE request and returns the 2xx response.</summary>
    Task<HttpResponseMessage> DeleteAsync(
        string path,
        RequestOptions? options,
        CancellationToken cancellationToken);
}
