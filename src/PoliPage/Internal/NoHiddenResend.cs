namespace PoliPage.Internal;

/// <summary>
/// Keeps SocketsHttpHandler from sending a request again behind the SDK's back.
/// </summary>
/// <remarks>
/// <para>
/// When a connection closes before any byte of the answer arrives, SocketsHttpHandler
/// silently sends the request again, up to three more times, on a new connection, if the
/// request has no content. It does so on a fresh connection too, not only on a reused
/// keep-alive one. Such a resend is not counted by <c>MaxRetries</c>, not reported to
/// <c>OnRequest</c> or <c>OnRetry</c>, and for the presigned download it breaks "the
/// download is not retried".
/// </para>
/// <para>
/// So every SDK request carries content: an empty body (<c>Content-Length: 0</c>) on GET
/// and DELETE, the JSON body on POST. The failure then surfaces as <c>network_error</c> and
/// goes through the SDK's own, counted retry (for the download, as <c>DOWNLOAD_FAILED</c>).
/// </para>
/// <para>
/// The handler also resends a request whose body still waits for <c>100 Continue</c>. The
/// SDK never asks for it, but an injected HttpClient whose
/// <c>DefaultRequestHeaders.ExpectContinue</c> is <see langword="true"/> adds it to every
/// request, and a request cannot opt out of a client default. Such a resend only happens
/// before any byte of the body left the client, so the API cannot have stored a render.
/// </para>
/// </remarks>
internal static class NoHiddenResend
{
    internal static void Apply(HttpRequestMessage request)
        => request.Content ??= new ByteArrayContent([]);
}
