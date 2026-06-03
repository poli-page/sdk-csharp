namespace PoliPage;

/// <summary>
/// Carries context about a single outgoing HTTP attempt so callers can observe
/// or instrument request behaviour via <see cref="PoliPageClientOptions.OnRequest"/>.
/// </summary>
/// <param name="Method">HTTP method (<c>GET</c>, <c>POST</c>, <c>DELETE</c>).</param>
/// <param name="Url">The absolute URL the request is sent to.</param>
/// <param name="Attempt">
/// One-based attempt number. <c>1</c> for the initial send, <c>2</c> for the first retry, etc.
/// </param>
public sealed record RequestEvent(string Method, string Url, int Attempt);
