using System.Net;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace PoliPage.Tests;

/// <summary>
/// Tests for the inline retry logic inside <c>HttpTransport.SendAndMapErrorsAsync</c>.
/// Despite the class name, no DelegatingHandler is involved — the retry loop lives
/// directly in HttpTransport (Phase 4 decision: inline, not DelegatingHandler).
/// </summary>
public sealed class RetryHandlerTests
{
    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private static readonly byte[] PdfMagicBytes = [0x25, 0x50, 0x44, 0x46]; // %PDF

    private sealed record TestHarness(WireMockServer Server, PoliPageClient Client) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }
    }

    /// <summary>
    /// Creates a harness with zero jitter (deterministic delays) and configurable retry options.
    /// </summary>
    private static TestHarness StartHarness(
        int maxRetries = 2,
        TimeSpan? retryDelay = null,
        TimeSpan? requestTimeout = null,
        Action<RetryEvent>? onRetry = null,
        Func<double>? jitter = null,
        HttpClient? httpClient = null)
    {
        var server = WireMockServer.Start();
        var options = new PoliPageClientOptions
        {
            ApiKey = "pp_test_unit",
            BaseUrl = new Uri(server.Url!),
            MaxRetries = maxRetries,
            RetryDelay = retryDelay ?? TimeSpan.FromMilliseconds(50),
            RequestTimeout = requestTimeout ?? TimeSpan.FromSeconds(10),
            OnRetry = onRetry,
            HttpClient = httpClient,
        };
        // Use the internal ctor so tests can inject a deterministic jitter.
        var client = jitter is not null
            ? new PoliPageClient(options, jitter)
            : new PoliPageClient(options);
        return new TestHarness(server, client);
    }

    private static ProjectModeInput DefaultInput() => new()
    {
        Project = "billing",
        Template = "invoice",
        Version = "1.0.0",
        Data = new { customer = "Acme" },
    };

    private static Task<byte[]> RenderAsync(PoliPageClient client, CancellationToken ct = default)
        => client.Render.PdfAsync(DefaultInput(), cancellationToken: ct);

    // WireMock.Net's initial scenario state is the absence of a WhenStateIs clause —
    // declaring WhenStateIs("0") (or any other value) on the first stub means it never
    // matches and WireMock returns its default 404. The first stub below omits
    // WhenStateIs deliberately; only the second stub gates on the post-transition state.
    private const string ScenarioStep2 = "step-2";

    /// <summary>
    /// Stubs WireMock to return <paramref name="errorStatus"/> on the first call and
    /// 200 with a JSON descriptor on the second call, plus the presigned storage GET
    /// that PdfAsync follows to fetch the actual bytes.
    /// </summary>
    private static void StubFailThenSucceed(WireMockServer server, int errorStatus, string? retryAfterHeader = null)
    {
        const string Scenario = "fail-then-succeed";
        const string DocumentId = "doc_retry";
        var presignedUrl = $"{server.Url}/storage/{DocumentId}.pdf";
        var descriptorJson = $$"""
            {
                "documentId": "{{DocumentId}}",
                "organizationId": "org_xyz",
                "environment": "test",
                "format": "pdf",
                "pageCount": 1,
                "sizeBytes": 100,
                "presignedPdfUrl": "{{presignedUrl}}"
            }
            """;

        var errorResponse = Response.Create().WithStatusCode(errorStatus);
        if (retryAfterHeader is not null)
            errorResponse = errorResponse.WithHeader("Retry-After", retryAfterHeader);

        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .InScenario(Scenario)
              .WillSetStateTo(ScenarioStep2)
              .RespondWith(errorResponse);

        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .InScenario(Scenario)
              .WhenStateIs(ScenarioStep2)
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(descriptorJson));

        server.Given(Request.Create().WithPath($"/storage/{DocumentId}.pdf").UsingGet())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/pdf")
                  .WithBody(PdfMagicBytes));
    }

    /// <summary>
    /// Filters WireMock's request log to just the POSTs to the render endpoint. PdfAsync
    /// follows up with a GET to the presigned storage URL on success, which inflates the
    /// raw LogEntries count beyond what most retry assertions care about.
    /// </summary>
    private static List<WireMock.Logging.ILogEntry> PostLogEntries(WireMockServer server)
        => server.LogEntries.Where(e => e.RequestMessage.Method == "POST").ToList();

    /// <summary>
    /// Stubs the two-step render flow with a fixed success response on every call:
    /// POST /v1/render → JSON descriptor; GET /storage/{id}.pdf → PDF bytes.
    /// Used by tests that need a custom HTTP handler (network-fail, timeout) instead
    /// of WireMock's scenario state machine to gate the failure.
    /// </summary>
    private static void StubRenderJsonAndStorage(WireMockServer server, string documentId = "doc_retry")
    {
        var presignedUrl = $"{server.Url}/storage/{documentId}.pdf";
        var descriptorJson = $$"""
            {
                "documentId": "{{documentId}}",
                "organizationId": "org_xyz",
                "environment": "test",
                "format": "pdf",
                "pageCount": 1,
                "sizeBytes": 100,
                "presignedPdfUrl": "{{presignedUrl}}"
            }
            """;

        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(descriptorJson));

        server.Given(Request.Create().WithPath($"/storage/{documentId}.pdf").UsingGet())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/pdf")
                  .WithBody(PdfMagicBytes));
    }

    // ------------------------------------------------------------------ //
    // 1. Retries on 500 then succeeds on second attempt
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Retries_on_500_then_succeeds_on_second_attempt()
    {
        using var harness = StartHarness();
        StubFailThenSucceed(harness.Server, 500);

        var result = await RenderAsync(harness.Client);

        result.Should().NotBeEmpty();
        result[..4].Should().Equal(0x25, 0x50, 0x44, 0x46); // %PDF
        PostLogEntries(harness.Server).Should().HaveCount(2, "one initial attempt + one retry");
    }

    // ------------------------------------------------------------------ //
    // 2. Retries on 429 then succeeds
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Retries_on_429_then_succeeds()
    {
        using var harness = StartHarness();
        StubFailThenSucceed(harness.Server, 429);

        var result = await RenderAsync(harness.Client);

        result.Should().NotBeEmpty();
        PostLogEntries(harness.Server).Should().HaveCount(2, "one initial attempt + one retry");
    }

    // ------------------------------------------------------------------ //
    // 3. Retries on 503 with max attempts exhausted → throws PoliPageException
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Retries_on_503_with_max_attempts_exhausted_throws()
    {
        // MaxRetries=2 → 3 total attempts (initial + 2 retries).
        using var harness = StartHarness(maxRetries: 2);
        harness.Server.Given(Request.Create().WithPath("/v1/render").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(503));

        var act = async () => await RenderAsync(harness.Client);

        await act.Should().ThrowAsync<PoliPageException>();
        PostLogEntries(harness.Server).Should().HaveCount(3, "1 initial + 2 retries before giving up");
    }

    // ------------------------------------------------------------------ //
    // 4. Does not retry on 400 — single attempt, PoliPageValidationException
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Does_not_retry_on_400()
    {
        using var harness = StartHarness(maxRetries: 5);
        harness.Server.Given(Request.Create().WithPath("/v1/render").UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(400)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(@"{""code"":""VALIDATION"",""message"":""bad input""}"));

        var act = async () => await RenderAsync(harness.Client);

        await act.Should().ThrowAsync<PoliPageValidationException>();
        PostLogEntries(harness.Server).Should().HaveCount(1, "400 must not trigger any retry");
    }

    // ------------------------------------------------------------------ //
    // 5. Does not retry on 404 — single attempt, PoliPageNotFoundException
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Does_not_retry_on_404()
    {
        using var harness = StartHarness(maxRetries: 5);
        harness.Server.Given(Request.Create().WithPath("/v1/render").UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(404)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(@"{""code"":""NOT_FOUND"",""message"":""template not found""}"));

        var act = async () => await RenderAsync(harness.Client);

        await act.Should().ThrowAsync<PoliPageNotFoundException>();
        PostLogEntries(harness.Server).Should().HaveCount(1, "404 must not trigger any retry");
    }

    // ------------------------------------------------------------------ //
    // 6. Retries on HttpRequestException (transient network failure) then succeeds
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Retries_on_HttpRequestException_then_succeeds()
    {
        // A custom DelegatingHandler that throws HttpRequestException on the first send,
        // then forwards to WireMock on subsequent sends.
        using var server = WireMockServer.Start();
        StubRenderJsonAndStorage(server);

        var callCount = 0;
        var networkFailHandler = new ThrowOnceHandler(
            inner: new HttpClientHandler(),
            throwCount: 1,
            onThrow: () => new HttpRequestException("Simulated transient network failure"));

        using var httpClient = new HttpClient(networkFailHandler);
        using var harness = StartHarness(maxRetries: 2, httpClient: httpClient);
        // The harness has its own server URL but we need the httpClient pointing at the server.
        // Re-create directly to set base URL correctly.
        harness.Dispose();

        var options = new PoliPageClientOptions
        {
            ApiKey = "pp_test_unit",
            BaseUrl = new Uri(server.Url!),
            MaxRetries = 2,
            RetryDelay = TimeSpan.FromMilliseconds(50),
            RequestTimeout = TimeSpan.FromSeconds(5),
            HttpClient = httpClient,
        };
        using var client = new PoliPageClient(options);

        var result = await client.Render.PdfAsync(DefaultInput());

        result.Should().NotBeEmpty();
        callCount.Should().Be(0, "the counter tracks nothing here; ThrowOnceHandler tracks internally");
        networkFailHandler.TotalCalls.Should().Be(2, "1 failed + 1 succeeded");

        server.Dispose();
    }

    // ------------------------------------------------------------------ //
    // 7. Retries on per-attempt timeout then succeeds
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Retries_on_timeout_then_succeeds()
    {
        // Why: WireMock's scenario state machine does not reliably transition when a
        // client disconnects mid-response (which is exactly what a timeout looks like).
        // Use a synthetic ThrowOnceHandler that throws TaskCanceledException on the
        // first send (simulating our timeout CTS firing) and forwards subsequent sends
        // to WireMock unchanged.
        using var server = WireMockServer.Start();
        StubRenderJsonAndStorage(server);

        var handler = new ThrowOnceHandler(
            inner: new HttpClientHandler(),
            throwCount: 1,
            onThrow: () => new TaskCanceledException("simulated per-attempt timeout"));
        using var httpClient = new HttpClient(handler);

        var options = new PoliPageClientOptions
        {
            ApiKey = "pp_test_unit",
            BaseUrl = new Uri(server.Url!),
            MaxRetries = 2,
            RetryDelay = TimeSpan.FromMilliseconds(10),
            HttpClient = httpClient,
        };
        using var client = new PoliPageClient(options);

        var result = await client.Render.PdfAsync(DefaultInput());

        result.Should().NotBeEmpty();
        handler.TotalCalls.Should().Be(2, "one timeout-throw + one success");
    }

    // ------------------------------------------------------------------ //
    // 8. Idempotency-Key is identical on every retry attempt
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Idempotency_Key_is_same_across_retries()
    {
        using var harness = StartHarness(maxRetries: 2);
        harness.Server.Given(Request.Create().WithPath("/v1/render").UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(500));

        var act = async () => await RenderAsync(harness.Client);
        await act.Should().ThrowAsync<PoliPageException>();

        var entries = harness.Server.LogEntries.ToList();
        entries.Should().HaveCount(3);

        // All three requests must carry the same Idempotency-Key.
        var keys = entries
            .Select(e => e.RequestMessage.Headers!["Idempotency-Key"].First())
            .Distinct()
            .ToList();
        keys.Should().HaveCount(1, "Idempotency-Key must be identical across all attempts");
    }

    // ------------------------------------------------------------------ //
    // 9. Honors Retry-After seconds from a 429 response
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Honors_Retry_After_seconds()
    {
        var retryDelays = new List<TimeSpan>();
        using var harness = StartHarness(
            maxRetries: 1,
            onRetry: evt => retryDelays.Add(evt.Delay),
            jitter: () => 1.0);

        // Server returns 429 with Retry-After: 1 on first call, 200 on second.
        StubFailThenSucceed(harness.Server, 429, retryAfterHeader: "1");

        await RenderAsync(harness.Client);

        retryDelays.Should().HaveCount(1);
        // Delay should be 1s (from Retry-After) — allow generous ±200ms tolerance
        // because Backoff caps the value and returns it exactly; the tolerance is for
        // the actual Task.Delay which may overshoot slightly.
        retryDelays[0].Should().BeCloseTo(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(200));
    }

    // ------------------------------------------------------------------ //
    // 10. Caps Retry-After at 30 seconds (verified via OnRetry, no actual wait)
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Caps_Retry_After_at_30_seconds()
    {
        // Why: the OnRetry hook fires BEFORE Task.Delay. Cancel from inside the hook to
        // short-circuit the 30s sleep — without this the test takes the full cap as
        // wall-clock time, even though the assertion only needs the captured value.
        var capturedDelay = TimeSpan.Zero;
        using var cts = new CancellationTokenSource();
        using var harness = StartHarness(
            maxRetries: 1,
            onRetry: evt =>
            {
                capturedDelay = evt.Delay;
                cts.Cancel();
            },
            jitter: () => 1.0);

        harness.Server.Given(Request.Create().WithPath("/v1/render").UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(429)
                   .WithHeader("Retry-After", "120"));

        var act = async () => await harness.Client.Render.PdfAsync(DefaultInput(), cancellationToken: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        capturedDelay.Should().Be(TimeSpan.FromSeconds(30), "Retry-After: 120 must be capped at MaxDelay (30s)");
    }

    // ------------------------------------------------------------------ //
    // 11. Backoff uses exponential growth (deterministic via jitter=1.0)
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Backoff_uses_exponential_growth()
    {
        var retryEvents = new List<RetryEvent>();
        // MaxRetries=3 → 3 retries → delays at attempts 1, 2, 3.
        using var harness = StartHarness(
            maxRetries: 3,
            retryDelay: TimeSpan.FromMilliseconds(100),
            onRetry: evt => retryEvents.Add(evt),
            jitter: () => 1.0);

        harness.Server.Given(Request.Create().WithPath("/v1/render").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(500));

        var act = async () => await RenderAsync(harness.Client);
        await act.Should().ThrowAsync<PoliPageException>();

        retryEvents.Should().HaveCount(3);
        // base=100ms, jitter=1.0: attempt 1 → 100ms, attempt 2 → 200ms, attempt 3 → 400ms
        retryEvents[0].Delay.Should().Be(TimeSpan.FromMilliseconds(100));
        retryEvents[1].Delay.Should().Be(TimeSpan.FromMilliseconds(200));
        retryEvents[2].Delay.Should().Be(TimeSpan.FromMilliseconds(400));
    }

    // ------------------------------------------------------------------ //
    // 12. OnRetry hook fires before each retry with correct attempt and reason
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task OnRetry_hook_fires_before_each_retry_with_correct_attempt_and_reason()
    {
        var events = new List<RetryEvent>();
        using var harness = StartHarness(
            maxRetries: 2,
            onRetry: evt => events.Add(evt));

        harness.Server.Given(Request.Create().WithPath("/v1/render").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(503));

        var act = async () => await RenderAsync(harness.Client);
        await act.Should().ThrowAsync<PoliPageException>();

        events.Should().HaveCount(2);

        // Attempt numbers are 1-based.
        events[0].Attempt.Should().Be(1);
        events[1].Attempt.Should().Be(2);

        // Status code should be 503 (the HTTP failure).
        events[0].StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        events[1].StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        // Reason should mention the HTTP status.
        events[0].Reason.Should().Contain("503");
        events[1].Reason.Should().Contain("503");
    }

    // ------------------------------------------------------------------ //
    // 13. OnRetry hook that throws does not break the request
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task OnRetry_hook_that_throws_does_not_break_the_request()
    {
        using var harness = StartHarness(
            maxRetries: 2,
            onRetry: _ => throw new InvalidOperationException("Hook exploded"));

        StubFailThenSucceed(harness.Server, 500);

        // Hook throws on retry, but the request should still succeed.
        var result = await RenderAsync(harness.Client);

        result.Should().NotBeEmpty();
        PostLogEntries(harness.Server).Should().HaveCount(2);
    }

    // ------------------------------------------------------------------ //
    // 14. Caller cancellation does not trigger retry
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Caller_cancellation_does_not_trigger_retry()
    {
        using var harness = StartHarness(maxRetries: 5);

        // Server holds the response for 1s so cancellation fires mid-send.
        harness.Server.Given(Request.Create().WithPath("/v1/render").UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/pdf")
                   .WithBody(PdfMagicBytes)
                   .WithDelay(TimeSpan.FromSeconds(1)));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var act = async () => await RenderAsync(harness.Client, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        // The semantic is "caller cancel stops the loop immediately, no retry happens."
        // The OperationCanceledException assertion above already proves no retry occurred
        // (a retry would have surfaced PoliPageException). We can't reliably assert
        // server.LogEntries.Count because WireMock may or may not log requests that
        // were aborted mid-flight; that's an implementation detail of the mock, not
        // a property of our retry logic.
        PostLogEntries(harness.Server).Should().HaveCountLessThanOrEqualTo(1);
    }

    // ------------------------------------------------------------------ //
    // 15. MaxRetries=0 disables retry entirely
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task MaxRetries_zero_disables_retry_entirely()
    {
        using var harness = StartHarness(maxRetries: 0);
        harness.Server.Given(Request.Create().WithPath("/v1/render").UsingPost())
               .RespondWith(Response.Create().WithStatusCode(500));

        var act = async () => await RenderAsync(harness.Client);
        await act.Should().ThrowAsync<PoliPageException>();

        PostLogEntries(harness.Server).Should().HaveCount(1, "MaxRetries=0 means no retries at all");
    }
}

/// <summary>
/// A <see cref="DelegatingHandler"/> that throws an <see cref="HttpRequestException"/>
/// on the first N sends, then forwards to the inner handler normally.
/// Used to simulate transient network failures in tests.
/// </summary>
internal sealed class ThrowOnceHandler : DelegatingHandler
{
    private readonly int _throwCount;
    private readonly Func<Exception> _onThrow;
    private int _calls;

    public int TotalCalls => _calls;

    internal ThrowOnceHandler(HttpMessageHandler inner, int throwCount, Func<Exception> onThrow)
        : base(inner)
    {
        _throwCount = throwCount;
        _onThrow = onThrow;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var callNumber = System.Threading.Interlocked.Increment(ref _calls);
        if (callNumber <= _throwCount)
            throw _onThrow();
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
