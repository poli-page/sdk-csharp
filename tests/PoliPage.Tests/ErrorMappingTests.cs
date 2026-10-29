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

    private static TestHarness StartServerAndClient(string apiKey = "pp_sandbox_unit")
    {
        var server = WireMockServer.Start();
        var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = apiKey,
            BaseUrl = new Uri(server.Url!),
            // Why: this class tests error-envelope parsing, not retry behavior.
            // MaxRetries=0 prevents retryable status codes (429, 5xx) from triggering
            // real backoff delays that would make the suite slow without adding coverage.
            MaxRetries = 0,
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

        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(response);
    }

    // The API's error body: { error, detail?, issues?, requestId }.
    private static string ErrorJson(string code, string detail, string? requestId = null)
    {
        if (requestId is not null)
            return $@"{{""error"":""{code}"",""detail"":""{detail}"",""requestId"":""{requestId}""}}";
        return $@"{{""error"":""{code}"",""detail"":""{detail}""}}";
    }

    private static RenderInput DefaultInput() => new()
    {
        Project = "billing",
        Template = "invoice",
        Data = new { customer = "Acme" },
    };

    private static Task<byte[]> CallRenderAsync(PoliPageClient client, CancellationToken ct = default)
        => client.Render.PdfAsync(DefaultInput(), cancellationToken: ct);

    // ------------------------------------------------------------------ //
    // 1. 400 → PoliPageValidationException
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_400_throws_PoliPageValidationException_with_VALIDATION_ERROR_code()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 400, ErrorJson("VALIDATION_ERROR", "template is required"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageValidationException>();
        ex.Which.Code.Should().Be("VALIDATION_ERROR");
        ex.Which.StatusCode.Should().Be(400);
        ex.Which.Message.Should().Be("template is required");
    }

    // ------------------------------------------------------------------ //
    // 2. 401 → PoliPageAuthException / INVALID_API_KEY
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_401_throws_PoliPageAuthException_with_INVALID_API_KEY_code()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 401, ErrorJson("INVALID_API_KEY", "Invalid API key"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageAuthException>();
        ex.Which.Code.Should().Be("INVALID_API_KEY");
        ex.Which.StatusCode.Should().Be(401);
        ex.Which.Message.Should().Be("Invalid API key");
    }

    // ------------------------------------------------------------------ //
    // 3. 402 has no dedicated subclass: payment grace is 403 ORGANIZATION_PAYMENT_GRACE
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_402_throws_base_PoliPageException_with_code_verbatim()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 402, ErrorJson("SOMETHING_NEW", "Outstanding balance"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageException>();
        ex.Which.GetType().Should().Be<PoliPageException>();
        ex.Which.Code.Should().Be("SOMETHING_NEW");
        ex.Which.StatusCode.Should().Be(402);
        Type.GetType("PoliPage.PoliPagePaymentRequiredException, PoliPage").Should().BeNull();
    }

    [Fact]
    public async Task Status_403_ORGANIZATION_PAYMENT_GRACE_throws_PoliPageAuthException()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 403, ErrorJson("ORGANIZATION_PAYMENT_GRACE", "Payment overdue"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageAuthException>();
        ex.Which.Code.Should().Be(PoliPageErrorCode.OrganizationPaymentGrace);
        ex.Which.Message.Should().Be("Payment overdue");
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
    // 6. 404 { error: TEMPLATE_NOT_FOUND, detail } → code from `error`, message from `detail`
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_404_with_error_TEMPLATE_NOT_FOUND_surfaces_that_code_and_the_detail_as_message()
    {
        using var harness = StartServerAndClient();
        StubError(
            harness.Server,
            404,
            @"{""error"":""TEMPLATE_NOT_FOUND"",""detail"":""Template 'invoice' not found in project 'billing'""}");

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageNotFoundException>();
        ex.Which.Code.Should().Be(PoliPageErrorCode.TemplateNotFound, "the code is read from the body, never inferred from the status");
        ex.Which.Message.Should().Be("Template 'invoice' not found in project 'billing'");
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
        StubError(harness.Server, 410, ErrorJson("DOCUMENT_GONE", "Document was deleted"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageGoneException>();
        ex.Which.Code.Should().Be(PoliPageErrorCode.DocumentGone);
        ex.Which.StatusCode.Should().Be(410);
    }

    // ------------------------------------------------------------------ //
    // 9. 422 → PoliPageValidationException
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_422_throws_PoliPageValidationException()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 422, ErrorJson("VALIDATION_ERROR", "data field must be an object"));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageValidationException>();
        ex.Which.Code.Should().Be("VALIDATION_ERROR");
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
            ErrorJson("QUOTA_EXCEEDED", "Too many requests"),
            extraHeaders: new Dictionary<string, string> { ["Retry-After"] = "15" });

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageRateLimitException>();
        ex.Which.Code.Should().Be("QUOTA_EXCEEDED");
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
            ErrorJson("QUOTA_EXCEEDED", "Too many requests"),
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
            ErrorJson("QUOTA_EXCEEDED", "Too many requests"),
            extraHeaders: new Dictionary<string, string> { ["Retry-After"] = retryAt });

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageRateLimitException>();
        // Allow ±2s tolerance for clock skew between test setup and assertion.
        ex.Which.RetryAfter.Should().BeCloseTo(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(2));
    }

    // ------------------------------------------------------------------ //
    // 13. 500 with no body → base PoliPageException, INTERNAL_ERROR code
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Status_500_throws_base_PoliPageException_with_INTERNAL_ERROR_code_when_body_missing()
    {
        using var harness = StartServerAndClient();
        // No body — just the status code.
        harness.Server.Given(Request.Create().WithPath("/v1/render").UsingPost())
                      .RespondWith(Response.Create().WithStatusCode(500));

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageException>();
        // Must be the exact base type, not a subclass.
        ex.Which.GetType().Should().Be<PoliPageException>();
        ex.Which.Code.Should().Be(PoliPageErrorCode.InternalError, "a body that is not JSON gives INTERNAL_ERROR");
        ex.Which.StatusCode.Should().Be(500);
        ex.Which.Message.Should().Be("HTTP 500");
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

        // Body parsing must not throw; a non-JSON body gives INTERNAL_ERROR with the real status.
        var ex = await act.Should().ThrowAsync<PoliPageException>();
        ex.Which.GetType().Should().Be<PoliPageException>();
        ex.Which.StatusCode.Should().Be(502);
        ex.Which.Code.Should().Be(PoliPageErrorCode.InternalError);
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
        StubError(harness.Server, 401, ErrorJson("INVALID_API_KEY", "Bad key", requestId: "req-abc-123"));

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
    // 18. Message falls back to "HTTP <status>" when the body has no detail, title or message
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Exception_message_falls_back_to_HTTP_status_when_body_has_no_message_field()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 400, @"{""error"":""VALIDATION_ERROR""}");

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageValidationException>();
        ex.Which.Message.Should().Be("HTTP 400");
    }

    // ------------------------------------------------------------------ //
    // 18b. Code and message precedence
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Code_field_wins_over_error_field()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 400, @"{""code"":""FROM_CODE"",""error"":""FROM_ERROR"",""detail"":""d""}");

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageValidationException>();
        ex.Which.Code.Should().Be("FROM_CODE");
    }

    [Fact]
    public async Task Legacy_code_and_message_body_is_still_read()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 401, @"{""code"":""INVALID_API_KEY"",""message"":""Invalid API key""}");

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageAuthException>();
        ex.Which.Code.Should().Be("INVALID_API_KEY");
        ex.Which.Message.Should().Be("Invalid API key");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    [InlineData(410)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task JSON_body_without_code_or_error_gives_unknown_error_whatever_the_status(int status)
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, status, @"{""detail"":""something went wrong""}");

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageException>();
        ex.Which.Code.Should().Be("unknown_error", "the SDK never infers a code from a status");
        ex.Which.StatusCode.Should().Be(status);
        ex.Which.Message.Should().Be("something went wrong");
    }

    [Fact]
    public async Task Message_prefers_detail_then_title_then_message()
    {
        using var harness = StartServerAndClient();
        StubError(harness.Server, 400, @"{""error"":""VALIDATION_ERROR"",""title"":""Bad Request"",""message"":""legacy""}");

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageValidationException>();
        ex.Which.Message.Should().Be("Bad Request", "title is used when detail is absent, before message");
    }

    [Fact]
    public async Task Message_prefers_detail_over_title_and_message()
    {
        using var harness = StartServerAndClient();
        StubError(
            harness.Server,
            400,
            @"{""error"":""VALIDATION_ERROR"",""detail"":""Unknown field: version"",""title"":""Bad Request"",""message"":""legacy""}");

        var act = async () => await CallRenderAsync(harness.Client);

        var ex = await act.Should().ThrowAsync<PoliPageValidationException>();
        ex.Which.Message.Should().Be("Unknown field: version");
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
            ApiKey = "pp_sandbox_unit",
            BaseUrl = new Uri("http://nonexistent.invalid"),
            RequestTimeout = TimeSpan.FromSeconds(5),
            // Why: testing error mapping, not retry behavior.
            MaxRetries = 0,
        });

        var act = async () => await client.Render.PdfAsync(DefaultInput());

        var ex = await act.Should().ThrowAsync<PoliPageNetworkException>();
        ex.Which.Code.Should().Be(PoliPageErrorCode.NetworkError);
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
        harness.Server.Given(Request.Create().WithPath("/v1/render").UsingPost())
                      .RespondWith(Response.Create()
                          .WithStatusCode(200)
                          .WithHeader("Content-Type", "application/pdf")
                          .WithBody([0x25, 0x50, 0x44, 0x46])
                          .WithDelay(TimeSpan.FromSeconds(2)));

        using var slowClient = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_sandbox_unit",
            BaseUrl = new Uri(harness.Server.Url!),
            RequestTimeout = TimeSpan.FromMilliseconds(150),
            // Why: testing timeout mapping, not retry behavior. Retrying a timeout would
            // also time out (server always delays 2s), adding unnecessary test duration.
            MaxRetries = 0,
        });

        var act = async () => await slowClient.Render.PdfAsync(DefaultInput());

        var ex = await act.Should().ThrowAsync<PoliPageException>();
        ex.Which.GetType().Should().Be<PoliPageException>(
            "timeout must surface as PoliPageException, not as OperationCanceledException");
        ex.Which.Code.Should().Be(PoliPageErrorCode.Timeout);
        ex.Which.Message.Should().Be("Request timed out after 150ms");
        ex.Which.StatusCode.Should().Be(0);
    }
}
