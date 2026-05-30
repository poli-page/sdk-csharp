using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace PoliPage.Tests;

/// <summary>
/// Tests for exception hierarchy construction, error envelope parsing, and
/// the inline error-mapping path in HttpTransport.
/// </summary>
public sealed class ErrorMappingTests
{
    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private sealed record TestHarness(WireMockServer Server, PoliPageClient Client) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }
    }

    private static TestHarness StartServerAndClient(string apiKey = "pp_test_unit")
    {
        var server = WireMockServer.Start();
        var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = apiKey,
            BaseUrl = new Uri(server.Url!),
        });
        return new TestHarness(server, client);
    }

    private static void StubError(
        WireMockServer server,
        int statusCode,
        string? jsonBody = null,
        string? contentType = "application/json",
        Dictionary<string, string>? extraHeaders = null,
        int delayMs = 0)
    {
        var response = Response.Create()
            .WithStatusCode(statusCode);

        if (jsonBody is not null && contentType is not null)
            response = response.WithBody(jsonBody).WithHeader("Content-Type", contentType);

        if (extraHeaders is not null)
            foreach (var (k, v) in extraHeaders)
                response = response.WithHeader(k, v);

        if (delayMs > 0)
            response = response.WithDelay(TimeSpan.FromMilliseconds(delayMs));

        server.Given(Request.Create().WithPath("/render").UsingPost())
              .RespondWith(response);
    }

    private static string ErrorJson(string code, string message, string? requestId = null)
    {
        if (requestId is not null)
            return $@"{{""code"":""{code}"",""message"":""{message}"",""requestId"":""{requestId}""}}";
        return $@"{{""code"":""{code}"",""message"":""{message}""}}";
    }

    private static ProjectModeInput DefaultInput() => new()
    {
        Project = "billing",
        Template = "invoice",
        Version = "1.0.0",
        Data = new { customer = "Acme" },
    };

    private static Task<byte[]> CallRenderAsync(PoliPageClient client, CancellationToken ct = default)
        => client.Render.PdfAsync(DefaultInput(), cancellationToken: ct);

    // ------------------------------------------------------------------ //
    // 1. 400 → PoliPageValidationException
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_400_throws_PoliPageValidationException_with_VALIDATION_code()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 400, ErrorJson("VALIDATION", "template is required"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageValidationException>();
        ex.Which.Code.Should().Be("VALIDATION");
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.Message.Should().Be("template is required");
    }

    // ------------------------------------------------------------------ //
    // 2. 401 → PoliPageAuthException / UNAUTHORIZED
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_401_throws_PoliPageAuthException_with_UNAUTHORIZED_code()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 401, ErrorJson("UNAUTHORIZED", "Invalid API key"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageAuthException>();
        ex.Which.Code.Should().Be("UNAUTHORIZED");
        ex.Which.StatusCode.Should().Be(401);
        ex.Which.Message.Should().Be("Invalid API key");
    }

    // ------------------------------------------------------------------ //
    // 3. 402 → PoliPagePaymentRequiredException
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_402_throws_PoliPagePaymentRequiredException()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 402, ErrorJson("PAYMENT_REQUIRED", "Outstanding balance"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPagePaymentRequiredException>();
        ex.Which.Code.Should().Be("PAYMENT_REQUIRED");
        ex.Which.StatusCode.Should().Be(402);
    }

    // ------------------------------------------------------------------ //
    // 4. 403 → PoliPageAuthException / FORBIDDEN
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_403_throws_PoliPageAuthException_with_FORBIDDEN_code()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 403, ErrorJson("FORBIDDEN", "Access denied"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageAuthException>();
        ex.Which.Code.Should().Be("FORBIDDEN");
        ex.Which.StatusCode.Should().Be(403);
    }

    // ------------------------------------------------------------------ //
    // 5. 404 → PoliPageNotFoundException / NOT_FOUND
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_404_throws_PoliPageNotFoundException_with_NOT_FOUND_code()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 404, ErrorJson("NOT_FOUND", "Template not found"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageNotFoundException>();
        ex.Which.Code.Should().Be("NOT_FOUND");
        ex.Which.StatusCode.Should().Be(404);
    }

    // ------------------------------------------------------------------ //
    // 6. 404 + VERSION_NOT_FOUND code preserved as PoliPageNotFoundException
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_404_with_VERSION_NOT_FOUND_code_still_maps_to_PoliPageNotFoundException()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 404, ErrorJson("VERSION_NOT_FOUND", "Version 9.9.9 not found"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageNotFoundException>();
        ex.Which.Code.Should().Be("VERSION_NOT_FOUND", "envelope code must be preserved");
        ex.Which.StatusCode.Should().Be(404);
    }

    // ------------------------------------------------------------------ //
    // 7. 404 + DOCUMENT_NOT_FOUND code preserved as PoliPageNotFoundException
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_404_with_DOCUMENT_NOT_FOUND_code_still_maps_to_PoliPageNotFoundException()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 404, ErrorJson("DOCUMENT_NOT_FOUND", "Document abc not found"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageNotFoundException>();
        ex.Which.Code.Should().Be("DOCUMENT_NOT_FOUND", "envelope code must be preserved");
        ex.Which.StatusCode.Should().Be(404);
    }

    // ------------------------------------------------------------------ //
    // 8. 410 → PoliPageGoneException
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_410_throws_PoliPageGoneException()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 410, ErrorJson("GONE", "Resource permanently removed"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageGoneException>();
        ex.Which.Code.Should().Be("GONE");
        ex.Which.StatusCode.Should().Be(410);
    }

    // ------------------------------------------------------------------ //
    // 9. 422 → PoliPageValidationException
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_422_throws_PoliPageValidationException()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 422, ErrorJson("VALIDATION", "data field must be an object"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageValidationException>();
        ex.Which.Code.Should().Be("VALIDATION");
        ex.Which.StatusCode.Should().Be(422);
    }

    // ------------------------------------------------------------------ //
    // 10. 429 + Retry-After: 15 → RetryAfter ≈ 15s
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_429_throws_PoliPageRateLimitException_with_RetryAfter_seconds()
    {
        using var harness = StartServerAndClient();
        StubError(
            harness.Server,
            429,
            ErrorJson("RATE_LIMIT", "Too many requests"),
            extraHeaders: new Dictionary<string, string> { ["Retry-After"] = "15" });

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageRateLimitException>();
        ex.Which.Code.Should().Be("RATE_LIMIT");
        ex.Which.StatusCode.Should().Be(429);
        ex.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(15));
    }

    // ------------------------------------------------------------------ //
    // 11. 429 + Retry-After: 120 → capped at 30s
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_429_caps_RetryAfter_at_30_seconds()
    {
        using var harness = StartServerAndClient();
        StubError(
            harness.Server,
            429,
            ErrorJson("RATE_LIMIT", "Too many requests"),
            extraHeaders: new Dictionary<string, string> { ["Retry-After"] = "120" });

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageRateLimitException>();
        ex.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(30), "RetryAfter must be capped at 30s per spec");
    }

    // ------------------------------------------------------------------ //
    // 12. 429 + Retry-After HTTP-date form
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_429_parses_Retry_After_HTTP_date()
    {
        using var harness = StartServerAndClient();

        // Set the Retry-After date to ~10 seconds in the future.
        var retryAt = DateTimeOffset.UtcNow.AddSeconds(10).ToString("R");
        StubError(
            harness.Server,
            429,
            ErrorJson("RATE_LIMIT", "Too many requests"),
            extraHeaders: new Dictionary<string, string> { ["Retry-After"] = retryAt });

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageRateLimitException>();
        // Allow ±2s tolerance for clock skew between test setup and assertion.
        ex.Which.RetryAfter.Should().BeCloseTo(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
    }

    // ------------------------------------------------------------------ //
    // 13. 500 with no body → base PoliPageException, UNKNOWN code
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_500_throws_base_PoliPageException_with_UNKNOWN_code_when_body_missing()
    {
        using var harness = StartServerAndClient();
        // No body — just the status code.
        harness.Server.Given(Request.Create().WithPath("/render").UsingPost())
                      .RespondWith(Response.Create().WithStatusCode(500));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageException>();
        // Must be the exact base type, not a subclass.
        ex.Which.GetType().Should().Be<PoliPageException>();
        ex.Which.Code.Should().Be(PoliPageErrorCode.Unknown);
        ex.Which.StatusCode.Should().Be(500);
    }

    // ------------------------------------------------------------------ //
    // 14. 502 with HTML body → base PoliPageException, no parsing exception
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_502_with_HTML_body_throws_base_PoliPageException()
    {
        using var harness = StartServerAndClient();
        StubError(
            harness.Server,
            502,
            "<html><body>Bad Gateway</body></html>",
            contentType: "text/html");

        var act = async () => await CallRenderAsync(harness.Client);

        // Body parsing must not throw; we fall back to status-based defaults.
        var ex = await act.Should().ThrowAsync<PoliPageException>();
        ex.Which.GetType().Should().Be<PoliPageException>();
        ex.Which.StatusCode.Should().Be(502);
        ex.Which.Code.Should().Be(PoliPageErrorCode.Unknown);
    }

    // ------------------------------------------------------------------ //
    // 15. 503 with unknown code preserved from envelope
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_503_preserves_envelope_code_when_provided()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 503, ErrorJson("MAINTENANCE", "Server is in maintenance mode"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageException>();
        ex.Which.Code.Should().Be("MAINTENANCE", "envelope code must be preserved even when not in our enum");
        ex.Which.StatusCode.Should().Be(503);
    }

    // ------------------------------------------------------------------ //
    // 16. RequestId populated from envelope
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Exception_carries_RequestId_from_envelope()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 401, ErrorJson("UNAUTHORIZED", "Bad key", requestId: "req-abc-123"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageAuthException>();
        ex.Which.RequestId.Should().Be("req-abc-123");
    }

    // ------------------------------------------------------------------ //
    // 17. RequestId falls back to X-Request-Id header when envelope lacks it
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Exception_carries_RequestId_from_X_Request_Id_header_when_envelope_lacks_it()
    {
        using var harness = StartServerAndClient();
        // Envelope has no requestId field.
        StubError(
            harness.Server,
            404,
            ErrorJson("NOT_FOUND", "Not found"),
            extraHeaders: new Dictionary<string, string> { ["X-Request-Id"] = "hdr-req-xyz" });

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageNotFoundException>();
        ex.Which.RequestId.Should().Be("hdr-req-xyz", "header fallback must be used when envelope has no requestId");
    }

    // ------------------------------------------------------------------ //
    // 18. Message falls back to ReasonPhrase when envelope lacks message
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Exception_message_falls_back_to_ReasonPhrase_when_envelope_lacks_message()
    {
        using var harness = StartServerAndClient();
        // Envelope has only code, no message field.
        StubError(harness.Server, 400, @"{""code"":""VALIDATION""}");

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageValidationException>();
        // WireMock returns "Bad Request" as the reason phrase for 400.
        ex.Which.Message.Should().NotBeNullOrEmpty("message must fall back to reason phrase or HTTP status string");
    }

    // ------------------------------------------------------------------ //
    // 19. Network error → PoliPageNetworkException wrapping HttpRequestException
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Network_error_throws_PoliPageNetworkException_with_inner_HttpRequestException()
    {
        // The .invalid TLD is reserved per RFC 2606 — guaranteed DNS failure.
        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test_unit",
            BaseUrl = new Uri("http://nonexistent.invalid"),
            RequestTimeout = TimeSpan.FromSeconds(5),
        });

        var act = async () => await client.Render.PdfAsync(DefaultInput());

        var ex = await act.Should().ThrowAsync<PoliPageNetworkException>();
        ex.Which.Code.Should().Be(PoliPageErrorCode.Network);
        ex.Which.StatusCode.Should().Be(0);
        ex.Which.InnerException.Should().BeOfType<HttpRequestException>();
    }

    // ------------------------------------------------------------------ //
    // 20. Per-request timeout → PoliPageException with Timeout code
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Per_request_timeout_throws_PoliPageException_with_Timeout_code()
    {
        using var harness = StartServerAndClient();

        // Server delays 2s; client times out after 150ms.
        harness.Server.Given(Request.Create().WithPath("/render").UsingPost())
                      .RespondWith(Response.Create()
                          .WithStatusCode(200)
                          .WithHeader("Content-Type", "application/pdf")
                          .WithBody([0x25, 0x50, 0x44, 0x46])
                          .WithDelay(TimeSpan.FromSeconds(2)));

        using var slowClient = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test_unit",
            BaseUrl = new Uri(harness.Server.Url!),
            RequestTimeout = TimeSpan.FromMilliseconds(150),
        });

        var act = async () => await slowClient.Render.PdfAsync(DefaultInput());

        var ex = await act.Should().ThrowAsync<PoliPageException>();
        ex.Which.GetType().Should().Be<PoliPageException>(
            "timeout must surface as PoliPageException, not as OperationCanceledException");
        ex.Which.Code.Should().Be(PoliPageErrorCode.Timeout);
        ex.Which.StatusCode.Should().Be(0);
    }
}
