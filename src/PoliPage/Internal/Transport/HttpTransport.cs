using System.Net;
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
    private readonly int _maxRetries;
    private readonly TimeSpan _retryDelay;
    private readonly Action<RetryEvent>? _onRetry;
    private readonly Action<Exception>? _onError;
    private readonly Action<RequestEvent>? _onRequest;
    private readonly Action<ResponseEvent>? _onResponse;
    private readonly Func<double> _jitter;

    private static readonly string UserAgent = $"poli-page-sdk-dotnet/{VersionInfo.Version}";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Wire-format convention: enums serialise as the canonical literal expected by the API.
        // - ThumbnailFormat / Orientation → camelCase lowercase ("png", "portrait")
        // - PageFormat → PascalCase verbatim ("A4", "Letter")
        // The generic JsonStringEnumConverter<TEnum> is the AOT-friendly variant per IL3050.
        Converters =
        {
            new JsonStringEnumConverter<ThumbnailFormat>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<Orientation>(JsonNamingPolicy.CamelCase),
            new JsonStringEnumConverter<PageFormat>(),
        },
    };

    internal HttpTransport(
        HttpClient httpClient,
        Uri baseAddress,
        string apiKey,
        TimeSpan defaultTimeout,
        int maxRetries,
        TimeSpan retryDelay,
        Action<RetryEvent>? onRetry = null,
        Action<Exception>? onError = null,
        Action<RequestEvent>? onRequest = null,
        Action<ResponseEvent>? onResponse = null,
        Func<double>? jitter = null)
    {
        _httpClient = httpClient;
        _baseAddress = baseAddress;
        _apiKey = apiKey;
        _defaultTimeout = defaultTimeout;
        _maxRetries = maxRetries;
        _retryDelay = retryDelay;
        _onRetry = onRetry;
        _onError = onError;
        _onRequest = onRequest;
        _onResponse = onResponse;
        _jitter = jitter ?? DefaultJitter;
    }

    // Random.Shared.NextDouble() returns [0,1); map to [0.5, 1.5).
    private static double DefaultJitter() => 0.5 + Random.Shared.NextDouble();

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> PostAsync(
        string path,
        object body,
        string idempotencyKey,
        RequestOptions? options,
        string accept,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        var effectiveTimeout = options?.RequestTimeout ?? _defaultTimeout;

        using var request = BuildPostRequest(path, body, idempotencyKey, options, accept);

        return await SendAndMapErrorsAsync(request, effectiveTimeout, completionOption, cancellationToken).ConfigureAwait(false);
    }

    private HttpRequestMessage BuildPostRequest(
        string path, object body, string idempotencyKey, RequestOptions? options, string accept)
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
        request.Headers.Accept.ParseAdd(accept);

        if (options?.Headers is not null)
        {
            foreach (var (key, value) in options.Headers)
                request.Headers.TryAddWithoutValidation(key, value);
        }

        return request;
    }

    private async Task<HttpResponseMessage> SendAndMapErrorsAsync(
        HttpRequestMessage request, TimeSpan attemptTimeout, HttpCompletionOption completionOption, CancellationToken callerToken)
    {
        Exception? lastException = null;
        HttpResponseMessage? response = null;

        for (var attempt = 0; attempt <= _maxRetries; attempt++)
        {
            // Re-build the request on retry: HttpRequestMessage cannot be sent twice.
            // For attempt 0 we use the original request directly; for retries we clone it.
            // Why: HttpContent can only be sent once per HttpRequestMessage instance.
            var cloned = attempt > 0;
            var attemptRequest = cloned ? CloneRequest(request) : request;

            try
            {
                // Fire OnRequest BEFORE sending — gives observers a chance to log the attempt
                // even if the send itself throws synchronously.
                SafeFireOnRequest(new RequestEvent(
                    attemptRequest.Method.Method,
                    attemptRequest.RequestUri?.AbsoluteUri ?? "",
                    attempt + 1));

                // Create a fresh timeout CTS per attempt so that a timed-out attempt
                // does not prevent subsequent retry attempts from running.
                using var timeoutCts = new CancellationTokenSource(attemptTimeout);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeoutCts.Token);

                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                (response, lastException) = await TrySendAsync(attemptRequest, completionOption, linkedCts.Token, callerToken).ConfigureAwait(false);

                if (response is { IsSuccessStatusCode: true })
                {
                    SafeFireOnResponse(new ResponseEvent(
                        (int)response.StatusCode,
                        response.Headers.TryGetValues("X-Request-Id", out var v) ? v.FirstOrDefault() : null,
                        ElapsedMilliseconds(t0)));
                    return response;
                }

                var canRetry = IsRetryable(response, lastException) && attempt < _maxRetries;
                if (!canRetry)
                    await ThrowFinalErrorAsync(response, lastException).ConfigureAwait(false);

                // callerToken passed directly so caller cancellation interrupts the sleep.
                await DelayBeforeRetryAsync(attempt, response, lastException, callerToken).ConfigureAwait(false);

                response?.Dispose();
                response = null;
            }
            finally
            {
                // Dispose cloned requests when we created them; originals are disposed by PostAsync.
                if (cloned)
                    attemptRequest.Dispose();
            }
        }

        // Unreachable: the loop either returns or throws. Satisfy the compiler.
        throw lastException ?? new InvalidOperationException("Retry loop exited unexpectedly.");
    }

    /// <summary>
    /// Attempts a single HTTP send. Returns (response, null) on HTTP response (any status),
    /// or (null, exception) on network/timeout failure. Re-throws on caller cancellation.
    /// </summary>
    private async Task<(HttpResponseMessage? Response, Exception? Exception)> TrySendAsync(
        HttpRequestMessage request,
        HttpCompletionOption completionOption,
        CancellationToken linkedToken,
        CancellationToken callerToken)
    {
        try
        {
            var response = await _httpClient.SendAsync(request, completionOption, linkedToken)
                .ConfigureAwait(false);
            return (response, null);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw; // Caller cancel: never retry, never wrap.
        }
        catch (TaskCanceledException tce) when (!callerToken.IsCancellationRequested)
        {
            // Why: the caller's token is not cancelled, so our internal timeout CTS fired.
            // Surface this as a deterministic Timeout error rather than an ambiguous cancellation.
            return (null, new PoliPageException(PoliPageErrorCode.Timeout, 0, "Request timed out.", innerException: tce));
        }
        catch (HttpRequestException hre)
        {
            return (null, new PoliPageNetworkException(PoliPageErrorCode.NetworkError, "Network error during request.", hre));
        }
    }

    /// <summary>
    /// Always throws. Surfaces the terminal failure after all retries are exhausted.
    /// </summary>
    private static async Task ThrowFinalErrorAsync(HttpResponseMessage? response, Exception? lastException)
    {
        if (response is not null)
        {
            // Why: pass CancellationToken.None to FromResponseAsync because the response body has
            // already been buffered (HttpCompletionOption.ResponseContentRead). Threading the
            // linkedToken here would mean a race where the timeout-CTS fires between SendAsync
            // returning and the error body parse running, swallowing the real HTTP failure.
            using (response)
            {
                throw await ErrorParsing.FromResponseAsync(response, CancellationToken.None).ConfigureAwait(false);
            }
        }

        throw lastException!;
    }

    /// <summary>
    /// Computes and waits out the backoff delay before the next retry attempt.
    /// Fires the <see cref="_onRetry"/> hook before sleeping.
    /// </summary>
    private async Task DelayBeforeRetryAsync(
        int attempt,
        HttpResponseMessage? response,
        Exception? lastException,
        CancellationToken callerToken)
    {
        var retryAfter = response is not null ? ErrorParsing.ParseRetryAfter(response) : null;
        var delay = Backoff.ComputeDelay(attempt + 1, _retryDelay, retryAfter, _jitter());

        var statusCode = response?.StatusCode;
        var reason = lastException is not null
            ? lastException.Message
            : $"HTTP {(int)response!.StatusCode}";
        SafeFireOnRetry(new RetryEvent(attempt + 1, delay, statusCode, reason));

        try
        {
            await Task.Delay(delay, callerToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException)
        {
            // Unreachable: Task.Delay uses callerToken, not the per-attempt timeout.
            // If callerToken fires, the previous catch handles it. This catch exists
            // only to prevent a bare TaskCanceledException from escaping.
        }
    }

    private void SafeFireOnRetry(RetryEvent evt)
    {
        if (_onRetry is null) return;
        try { _onRetry(evt); }
        catch
        {
            // Why: hooks must never break the request — swallow all exceptions.
        }
    }

    private void SafeFireOnRequest(RequestEvent evt)
    {
        if (_onRequest is null) return;
        try { _onRequest(evt); }
        catch
        {
            // Hooks must not break the request.
        }
    }

    private void SafeFireOnResponse(ResponseEvent evt)
    {
        if (_onResponse is null) return;
        try { _onResponse(evt); }
        catch
        {
            // Hooks must not break the request.
        }
    }

    private static long ElapsedMilliseconds(long startTimestamp)
    {
        var elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp;
        return elapsed * 1000L / System.Diagnostics.Stopwatch.Frequency;
    }

    private static bool IsRetryable(HttpResponseMessage? response, Exception? exception)
    {
        if (exception is PoliPageNetworkException or PoliPageException { Code: PoliPageErrorCode.Timeout })
            return true;
        if (response is null) return false;
        var status = (int)response.StatusCode;
        return status == 429 || status >= 500;
    }

    /// <summary>
    /// Creates a fresh <see cref="HttpRequestMessage"/> with the same method, URI,
    /// headers, and body as <paramref name="original"/>. Required because an
    /// <see cref="HttpRequestMessage"/> (and its content) can only be sent once.
    /// </summary>
    private static HttpRequestMessage CloneRequest(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
            VersionPolicy = original.VersionPolicy,
        };

        foreach (var header in original.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        if (original.Content is StringContent originalContent)
        {
            // Why: HttpContent can only be sent once. We can't reuse a StringContent across attempts;
            // re-construct it from the original encoded bytes. ReadAsStringAsync on a StringContent
            // is synchronous internally — the string was encoded at construction time.
            var body = originalContent.ReadAsStringAsync().GetAwaiter().GetResult();
            var mediaType = originalContent.Headers.ContentType?.MediaType ?? "application/json";
            var charSet = originalContent.Headers.ContentType?.CharSet ?? "utf-8";
            var encoding = Encoding.GetEncoding(charSet);
            clone.Content = new StringContent(body, encoding, mediaType);

            foreach (var ch in originalContent.Headers)
            {
                if (!string.Equals(ch.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                    clone.Content.Headers.TryAddWithoutValidation(ch.Key, ch.Value);
            }
        }

        return clone;
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

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> GetAsync(
        string path,
        RequestOptions? options,
        string accept,
        HttpCompletionOption completionOption,
        CancellationToken cancellationToken)
    {
        var effectiveTimeout = options?.RequestTimeout ?? _defaultTimeout;
        using var request = BuildBodylessRequest(HttpMethod.Get, path, options, accept);
        return await SendAndMapErrorsAsync(request, effectiveTimeout, completionOption, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<HttpResponseMessage> DeleteAsync(string path, RequestOptions? options, CancellationToken cancellationToken)
    {
        var effectiveTimeout = options?.RequestTimeout ?? _defaultTimeout;
        using var request = BuildBodylessRequest(HttpMethod.Delete, path, options, "application/json");
        return await SendAndMapErrorsAsync(request, effectiveTimeout, HttpCompletionOption.ResponseContentRead, cancellationToken)
            .ConfigureAwait(false);
    }

    private HttpRequestMessage BuildBodylessRequest(
        HttpMethod method, string path, RequestOptions? options, string accept)
    {
        var uri = ComposeUri(_baseAddress, path);
        var request = new HttpRequestMessage(method, uri);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd(accept);
        // GET and DELETE are idempotent by HTTP semantics; no Idempotency-Key needed.

        if (options?.Headers is not null)
        {
            foreach (var (key, value) in options.Headers)
                request.Headers.TryAddWithoutValidation(key, value);
        }

        return request;
    }
}
