namespace PoliPage.Internal;

internal interface ITransport
{
    /// <summary>Sends a POST request and returns the raw response.</summary>
    Task<HttpResponseMessage> PostAsync(string path, object body, string idempotencyKey, RequestOptions? options, CancellationToken cancellationToken);

    /// <summary>Sends a GET request and returns the raw response.</summary>
    // Phase 6: wire up the implementation when Documents namespace is added.
    Task<HttpResponseMessage> GetAsync(string path, RequestOptions? options, CancellationToken cancellationToken);

    /// <summary>Sends a DELETE request.</summary>
    // Phase 6: wire up the implementation when Documents namespace is added.
    Task DeleteAsync(string path, RequestOptions? options, CancellationToken cancellationToken);
}
