using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PoliPage.Internal;

internal sealed class HttpTransport : ITransport
{
    private readonly HttpClient _httpClient;
    private readonly Uri _baseAddress;
    private readonly string _apiKey;
    private readonly TimeSpan _defaultTimeout;

    private static readonly string UserAgent = $"poli-page-sdk-dotnet/{VersionInfo.Version}";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    internal HttpTransport(HttpClient httpClient, Uri baseAddress, string apiKey, TimeSpan defaultTimeout)
    {
        _httpClient = httpClient;
        _baseAddress = baseAddress;
        _apiKey = apiKey;
        _defaultTimeout = defaultTimeout;
    }

    public async Task<HttpResponseMessage> PostAsync(
        string path,
        object body,
        string idempotencyKey,
        RequestOptions? options,
        CancellationToken cancellationToken)
    {
        var effectiveTimeout = options?.RequestTimeout ?? _defaultTimeout;

        using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        using var request = BuildPostRequest(path, body, idempotencyKey, options);

        return await SendAndMapErrorsAsync(request, linkedCts.Token, cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage BuildPostRequest(
        string path, object body, string idempotencyKey, RequestOptions? options)
    {
        var uri = ComposeUri(_baseAddress, path);

        // Why: Phase 8 will replace this with JsonSerializerContext (source-gen) for full
        // AOT/trim safety. Until then, suppress the IL2026/IL3050 diagnostics — the
        // dynamic serialization is only a concern for AOT-published apps, not for the
        // default JIT runtime that the current SDK targets.
#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.Serialize(body, JsonOptions);
#pragma warning restore IL2026, IL3050
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        request.Headers.Accept.ParseAdd("application/pdf");

        if (options?.Headers is not null)
        {
            foreach (var (key, value) in options.Headers)
                request.Headers.TryAddWithoutValidation(key, value);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAndMapErrorsAsync(
        HttpRequestMessage request, CancellationToken linkedToken, CancellationToken callerToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, linkedToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException hre)
        {
            throw new PoliPageNetworkException(PoliPageErrorCode.Network, "Network error during request.", hre);
        }
        catch (TaskCanceledException tce) when (!callerToken.IsCancellationRequested)
        {
            // Why: the caller's token is not cancelled, so our internal timeout CTS fired.
            // Surface this as a deterministic Timeout error rather than an ambiguous cancellation.
            throw new PoliPageException(PoliPageErrorCode.Timeout, 0, "Request timed out.", innerException: tce);
        }
        // Caller-cancelled TaskCanceledException flows through unchanged as OperationCanceledException.

        if (!response.IsSuccessStatusCode)
        {
            // Why: pass CancellationToken.None to FromResponseAsync because the response body has
            // already been buffered (HttpCompletionOption.ResponseContentRead above). Threading the
            // linkedToken here would mean a race where the timeout-CTS fires between SendAsync
            // returning and the error body parse running, swallowing the real HTTP failure.
            using (response)
            {
                throw await ErrorParsing.FromResponseAsync(response, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        return response;
    }

    // Why: `new Uri(base, "/render")` silently drops base path segments when the relative
    // starts with '/' (RFC 3986 §5.2). That breaks any caller passing a versioned base URL
    // such as `https://api.poli.page/v1/`. Normalize: ensure base ends with '/' and the
    // relative does NOT start with '/', then compose deterministically.
    internal static Uri ComposeUri(Uri baseAddress, string relativePath)
    {
        var baseStr = baseAddress.AbsoluteUri;
        if (!baseStr.EndsWith('/'))
            baseStr += "/";
        var relative = relativePath.TrimStart('/');
        return new Uri(new Uri(baseStr), relative);
    }

    public Task<HttpResponseMessage> GetAsync(string path, RequestOptions? options, CancellationToken cancellationToken)
    {
        // Phase 6: implement when Documents namespace is added.
        throw new NotSupportedException("GetAsync is not yet available. See Phase 6.");
    }

    public Task DeleteAsync(string path, RequestOptions? options, CancellationToken cancellationToken)
    {
        // Phase 6: implement when Documents namespace is added.
        throw new NotSupportedException("DeleteAsync is not yet available. See Phase 6.");
    }
}
