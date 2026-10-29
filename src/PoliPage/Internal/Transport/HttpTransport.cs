using System.Diagnostics;
using System.Net.Http.Headers;
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
    public Task<HttpResponseMessage> PostAsync(
        string path,
        object body,
        string idempotencyKey,
        RequestOptions? options,
        string accept,
        CancellationToken cancellationToken)
    {
        // Why: Phase 8 will replace this with JsonSerializerContext (source-gen) for full
        // AOT/trim safety. Until then, suppress the IL2026/IL3050 diagnostics — the
        // dynamic serialization is only a concern for AOT-published apps, not for the
        // default JIT runtime that the current SDK targets.
#pragma warning disable IL2026, IL3050
        var json = JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions);
#pragma warning restore IL2026, IL3050

        return SendAsync(HttpMethod.Post, path, json, idempotencyKey, options, accept, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<HttpResponseMessage> GetAsync(
        string path,
        RequestOptions? options,
        string accept,
        CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Get, path, jsonBody: null, idempotencyKey: null, options, accept, cancellationToken);

    /// <inheritdoc/>
    public Task<HttpResponseMessage> DeleteAsync(string path, RequestOptions? options, CancellationToken cancellationToken)
        => SendAsync(HttpMethod.Delete, path, jsonBody: null, idempotencyKey: null, options, "application/json", cancellationToken);

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        byte[]? jsonBody,
        string? idempotencyKey,
        RequestOptions? options,
        string accept,
        CancellationToken callerToken)
    {
        try
        {
            return await SendWithRetriesAsync(method, path, jsonBody, idempotencyKey, options, accept, callerToken)
                .ConfigureAwait(false);
        }
        catch (PoliPageException ex)
        {
            // OnError fires once, with the error the SDK is about to throw: an API error, a
            // network failure, a timeout, or `aborted` when the caller cancelled.
            SafeFireOnError(ex);
            throw;
        }
    }

    private async Task<HttpResponseMessage> SendWithRetriesAsync(
        HttpMethod method,
        string path,
        byte[]? jsonBody,
        string? idempotencyKey,
        RequestOptions? options,
        string accept,
        CancellationToken callerToken)
    {
        var attemptTimeout = options?.RequestTimeout ?? _defaultTimeout;
        var uri = ComposeUri(_baseAddress, path);

        for (var attempt = 1; ; attempt++)
        {
            var outcome = await SendOnceAsync(
                    method, uri, jsonBody, idempotencyKey, options, accept, attempt, attemptTimeout, callerToken)
                .ConfigureAwait(false);
            if (outcome.Response is { } response)
                return response;

            var error = outcome.Error!;
            if (!outcome.Retryable || attempt > _maxRetries)
                throw error;

            // A Retry-After header (even "0") sets the delay, without jitter; otherwise retry
            // n (= the failed attempt's number) waits retryDelay × 2^(n-1) × jitter.
            var delay = Backoff.ComputeDelay(attempt, _retryDelay, outcome.RetryAfter, _jitter());
            SafeFireOnRetry(new RetryEvent(
                attempt + 1,
                delay,
                error.StatusCode == 0 ? null : (System.Net.HttpStatusCode)error.StatusCode,
                error.Message));

            await SleepAsync(delay, callerToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// One attempt: sends the request and reads the whole answer, both under the attempt
    /// timeout. Returns the 2xx answer (its body in memory) or the attempt's error; throws
    /// <c>aborted</c> at once when the caller cancelled.
    /// </summary>
    private async Task<AttemptOutcome> SendOnceAsync(
        HttpMethod method,
        Uri uri,
        byte[]? jsonBody,
        string? idempotencyKey,
        RequestOptions? options,
        string accept,
        int attempt,
        TimeSpan attemptTimeout,
        CancellationToken callerToken)
    {
        // A new HttpRequestMessage per attempt: one can be sent only once.
        using var request = BuildRequest(method, uri, jsonBody, idempotencyKey, options, accept);

        // Fire OnRequest BEFORE sending — gives observers a chance to log the attempt
        // even if the send itself throws synchronously.
        SafeFireOnRequest(new RequestEvent(method.Method, uri.AbsoluteUri, attempt));

        // A fresh timeout per attempt, so a timed-out attempt does not prevent the retries.
        // It bounds the connection, the headers and the body together.
        using var timeoutCts = new CancellationTokenSource(attemptTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeoutCts.Token);
        var attemptToken = linkedCts.Token;
        var t0 = Stopwatch.GetTimestamp();

        HttpResponseMessage response;
        try
        {
            var send = _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, attemptToken);
            response = await BoundAsync(send, attemptToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (SdkErrors.IsTransportFailure(ex) || callerToken.IsCancellationRequested)
        {
            return AttemptOutcome.Failed(TransportFailure(ex, timeoutCts, attemptTimeout, callerToken), retryable: true);
        }

        return await ReadAnswerAsync(response, t0, timeoutCts, attemptTimeout, attemptToken, callerToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads the whole answer inside the attempt, under its timeout: a body cut or stalled
    /// mid-way is this attempt's failure, never a raw exception once the caller parses it.
    /// </summary>
    private async Task<AttemptOutcome> ReadAnswerAsync(
        HttpResponseMessage response,
        long t0,
        CancellationTokenSource timeoutCts,
        TimeSpan attemptTimeout,
        CancellationToken attemptToken,
        CancellationToken callerToken)
    {
        var status = (int)response.StatusCode;
        byte[] body;
        try
        {
            var read = response.Content.ReadAsByteArrayAsync(attemptToken);
            body = await BoundAsync(read, attemptToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (SdkErrors.IsTransportFailure(ex) || callerToken.IsCancellationRequested)
        {
            response.Dispose();
            if (response.IsSuccessStatusCode || callerToken.IsCancellationRequested)
            {
                // A 2xx whose body fails: network_error or timeout, retried; a cancellation:
                // aborted, whatever the status.
                return AttemptOutcome.Failed(TransportFailure(ex, timeoutCts, attemptTimeout, callerToken), retryable: true);
            }

            // An error answer whose body fails keeps its status, like a body that is not
            // JSON: INTERNAL_ERROR, retried according to the status.
            return ApiFailure(ErrorParsing.FromUnreadableBody(status, response.Headers, ex), response.Headers);
        }

        if (response.IsSuccessStatusCode)
        {
            ReplaceContent(response, body);
            // After the body is read: DurationMs covers it, and a 2xx whose body failed
            // never fires the hook.
            SafeFireOnResponse(new ResponseEvent(
                status,
                response.Headers.TryGetValues("X-Request-Id", out var v) ? v.FirstOrDefault() : null,
                ElapsedMilliseconds(t0)));
            return AttemptOutcome.Succeeded(response);
        }

        using (response)
        {
            return ApiFailure(ErrorParsing.FromBody(status, response.Headers, body), response.Headers);
        }
    }

    /// <summary>
    /// The error for an attempt that got no complete answer. The caller's cancellation
    /// throws <c>aborted</c> at once (never retried); otherwise a timeout states the bound
    /// that fired, and anything else is a <c>network_error</c>.
    /// </summary>
    private PoliPageException TransportFailure(
        Exception ex, CancellationTokenSource timeoutCts, TimeSpan attemptTimeout, CancellationToken callerToken)
    {
        if (callerToken.IsCancellationRequested)
            throw SdkErrors.Aborted(ex);
        if (timeoutCts.IsCancellationRequested)
            return SdkErrors.Timeout(attemptTimeout, ex);
        if (ex is TaskCanceledException { InnerException: TimeoutException })
        {
            // The injected HttpClient's own Timeout fired before the SDK's: name that bound.
            return SdkErrors.Timeout(_httpClient.Timeout, ex);
        }

        return SdkErrors.NetworkError(ex);
    }

    private static AttemptOutcome ApiFailure(PoliPageException error, HttpResponseHeaders headers)
    {
        // One rule for the retry loop and for IsRetryable(): 5xx and 429 are retried, except
        // the codes that waiting cannot fix (quota, storage access). Only Retry-After is read.
        var retryable = error.IsRetryable();
        var retryAfter = retryable ? RetryAfterParser.Parse(headers, DateTimeOffset.UtcNow) : null;
        return AttemptOutcome.Failed(error, retryable, retryAfter);
    }

    /// <summary>
    /// Awaits <paramref name="task"/>, but returns at once when <paramref name="token"/>
    /// fires, even through a handler that ignores cancellation, so the attempt timeout and
    /// the caller's cancellation hold whatever HttpClient the caller injected. An
    /// abandoned response is disposed when it arrives.
    /// </summary>
    private static async Task<T> BoundAsync<T>(Task<T> task, CancellationToken token)
    {
        try
        {
            return await task.WaitAsync(token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!task.IsCompleted)
        {
            _ = task.ContinueWith(
                static t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion && t.Result is IDisposable disposable)
                        disposable.Dispose();
                    _ = t.Exception; // observed: never an unobserved task exception
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    /// <summary>
    /// Waits <paramref name="delay"/>, never less: a timer that wakes early waits again for
    /// the rest, measured on the monotonic clock. A cancellation ends the wait at once with
    /// <c>aborted</c>.
    /// </summary>
    internal static async Task SleepAsync(TimeSpan delay, CancellationToken callerToken)
    {
        var end = Stopwatch.GetTimestamp() + (long)Math.Ceiling(delay.TotalSeconds * Stopwatch.Frequency);
        while (true)
        {
            var left = end - Stopwatch.GetTimestamp();
            if (left <= 0)
                return;

            var leftMs = Math.Ceiling(left * 1000.0 / Stopwatch.Frequency);
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(leftMs), callerToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (callerToken.IsCancellationRequested)
            {
                throw SdkErrors.Aborted(ex);
            }
        }
    }

    private HttpRequestMessage BuildRequest(
        HttpMethod method, Uri uri, byte[]? jsonBody, string? idempotencyKey, RequestOptions? options, string accept)
    {
        var request = new HttpRequestMessage(method, uri);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Accept.ParseAdd(accept);
        // On POST only (spec §9.3): GET and DELETE are idempotent by HTTP semantics.
        if (idempotencyKey is not null)
            request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);

        if (options?.Headers is not null)
        {
            foreach (var (key, value) in options.Headers)
                request.Headers.TryAddWithoutValidation(key, value);
        }

        if (jsonBody is not null)
        {
            var content = new ByteArrayContent(jsonBody);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            request.Content = content;
        }

        NoHiddenResend.Apply(request);
        return request;
    }

    /// <summary>Hands the caller the 2xx answer with its body already in memory.</summary>
    private static void ReplaceContent(HttpResponseMessage response, byte[] body)
    {
        var original = response.Content;
        var buffered = new ByteArrayContent(body);
        foreach (var header in original.Headers)
        {
            // The length is the buffered body's (automatic decompression changes it).
            if (!string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                buffered.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        response.Content = buffered;
        original.Dispose();
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

    private void SafeFireOnError(Exception evt)
    {
        if (_onError is null) return;
        try { _onError(evt); }
        catch
        {
            // Hooks must not break the request — original exception is already in flight.
        }
    }

    private static long ElapsedMilliseconds(long startTimestamp)
    {
        var elapsed = Stopwatch.GetTimestamp() - startTimestamp;
        return elapsed * 1000L / Stopwatch.Frequency;
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

    private sealed class AttemptOutcome
    {
        private AttemptOutcome(HttpResponseMessage? response, PoliPageException? error, bool retryable, TimeSpan? retryAfter)
        {
            Response = response;
            Error = error;
            Retryable = retryable;
            RetryAfter = retryAfter;
        }

        public HttpResponseMessage? Response { get; }
        public PoliPageException? Error { get; }
        public bool Retryable { get; }
        public TimeSpan? RetryAfter { get; }

        public static AttemptOutcome Succeeded(HttpResponseMessage response) => new(response, null, retryable: false, null);

        public static AttemptOutcome Failed(PoliPageException error, bool retryable, TimeSpan? retryAfter = null)
            => new(null, error, retryable, retryAfter);
    }
}
