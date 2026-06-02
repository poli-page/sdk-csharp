using System.Text.Json;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace PoliPage.Tests;

public sealed class RenderTests
{
    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private static readonly byte[] PdfMagicBytes = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34]; // %PDF-1.4

    /// <summary>
    /// Owns a WireMockServer + PoliPageClient pair for the duration of a test.
    /// Both are released together via <see cref="Dispose"/> — a leaked server
    /// keeps a TCP listener open and risks port exhaustion under CI.
    /// </summary>
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

    /// <summary>
    /// Registers two stubs that simulate the deployed API's render flow:
    /// 1. <c>POST /v1/render</c> → JSON <see cref="DocumentDescriptor"/> with a
    ///    <c>presignedPdfUrl</c> pointing back at the same WireMock instance.
    /// 2. <c>GET /storage/{documentId}.pdf</c> → the actual PDF bytes.
    ///
    /// This mirrors the real two-step pattern: the API always returns a descriptor,
    /// the PDF bytes are fetched from the presigned URL via the header-less download
    /// transport. See sdk-node/src/render.ts:78-114 for the reference flow.
    /// </summary>
    private static void StubRender(WireMockServer server, byte[]? body = null, string documentId = "doc_abc123")
    {
        var presignedUrl = $"{server.Url}/storage/{documentId}.pdf";
        var descriptorJson = $$"""
            {
                "documentId": "{{documentId}}",
                "organizationId": "org_xyz",
                "projectSlug": "billing",
                "templateSlug": "invoice",
                "version": "1.0.0",
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
                  .WithBody(body ?? PdfMagicBytes));
    }

    /// <summary>
    /// Returns the single POST log entry from the WireMock server. PdfAsync /
    /// PdfStreamAsync now issue two requests (POST /v1/render + GET storage),
    /// so callers asserting on the POST need to filter.
    /// </summary>
    private static WireMock.Logging.ILogEntry PostEntry(WireMockServer server)
    {
        return server.LogEntries
            .Where(e => e.RequestMessage.Method == "POST")
            .Should().ContainSingle("there should be exactly one POST request to the API")
            .Subject;
    }

    private static ProjectModeInput DefaultInput() => new()
    {
        Project = "billing",
        Template = "invoice",
        Version = "1.0.0",
        Data = new { customer = "Acme" },
    };

    /// <summary>
    /// Helper that retrieves the first value for a header from a WireMock request.
    /// Asserts the header is present.
    /// </summary>
    private static string GetSingleHeader(IDictionary<string, WireMock.Types.WireMockList<string>> headers, string name)
    {
        headers.Should().ContainKey(name, $"request must include header '{name}'");
        return headers[name].Should().ContainSingle().Subject;
    }

    // ------------------------------------------------------------------ //
    // 1. Happy path — bytes returned
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_returns_bytes_on_200_with_application_pdf()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        var pdf = await client.Render.PdfAsync(DefaultInput());

        pdf.Should().NotBeEmpty();
        pdf[..4].Should().Equal(0x25, 0x50, 0x44, 0x46); // %PDF
    }

    // ------------------------------------------------------------------ //
    // 2. Method + path
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_sends_POST_to_render()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        await client.Render.PdfAsync(DefaultInput());

        var entry = PostEntry(server);
        entry.RequestMessage.Method.Should().Be("POST");
        entry.RequestMessage.Path.Should().Be("/v1/render");
    }

    // ------------------------------------------------------------------ //
    // 3. Authorization header
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_sends_Authorization_Bearer_header()
    {
        using var harness = StartServerAndClient(apiKey: "pp_test_unit");
        var (server, client) = harness;
        StubRender(server);

        await client.Render.PdfAsync(DefaultInput());

        var entry = PostEntry(server);
        var value = GetSingleHeader(entry.RequestMessage.Headers!, "Authorization");
        value.Should().Be("Bearer pp_test_unit");
    }

    // ------------------------------------------------------------------ //
    // 4. User-Agent header
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_sends_User_Agent_header()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        await client.Render.PdfAsync(DefaultInput());

        var entry = PostEntry(server);
        var ua = GetSingleHeader(entry.RequestMessage.Headers!, "User-Agent");
        ua.Should().StartWith("poli-page-sdk-dotnet/");
    }

    // ------------------------------------------------------------------ //
    // 5. Accept: application/json on the POST (the API always returns a descriptor)
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_sends_Accept_application_json_on_render_POST()
    {
        // The Poli Page API's /v1/render endpoint always returns a JSON DocumentDescriptor;
        // the PDF bytes come from the subsequent presigned-URL fetch. Sending
        // Accept: application/json on the POST matches that contract.
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        await client.Render.PdfAsync(DefaultInput());

        var entry = PostEntry(server);
        entry.RequestMessage.Headers.Should().ContainKey("Accept");
        entry.RequestMessage.Headers!["Accept"].Should().Contain("application/json");
    }

    // ------------------------------------------------------------------ //
    // 6. Content-Type: application/json
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_sends_Content_Type_application_json()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        await client.Render.PdfAsync(DefaultInput());

        var entry = PostEntry(server);
        var ct = GetSingleHeader(entry.RequestMessage.Headers!, "Content-Type");
        ct.Should().StartWith("application/json");
    }

    // ------------------------------------------------------------------ //
    // 7. Auto-generated Idempotency-Key is a valid GUID
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_sends_auto_generated_Idempotency_Key_when_not_provided()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        await client.Render.PdfAsync(DefaultInput());

        var entry = PostEntry(server);
        var key = GetSingleHeader(entry.RequestMessage.Headers!, "Idempotency-Key");
        Guid.TryParse(key, out var parsed).Should().BeTrue("the auto-generated key must be a valid UUID");
        parsed.Should().NotBe(Guid.Empty);
    }

    // ------------------------------------------------------------------ //
    // 8. RequestOptions.IdempotencyKey override
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_uses_RequestOptions_IdempotencyKey_override_when_provided()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        await client.Render.PdfAsync(DefaultInput(), new RequestOptions { IdempotencyKey = "inv-INV-001" });

        var entry = PostEntry(server);
        var key = GetSingleHeader(entry.RequestMessage.Headers!, "Idempotency-Key");
        key.Should().Be("inv-INV-001");
    }

    // ------------------------------------------------------------------ //
    // 9. Body serialized as camelCase JSON
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_serializes_body_as_camelCase_JSON()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        var input = new ProjectModeInput
        {
            Project = "billing",
            Template = "invoice",
            Version = "2.0.0",
            Data = new { customerName = "Acme Corp" },
        };

        await client.Render.PdfAsync(input);

        var entry = PostEntry(server);
        var rawBody = entry.RequestMessage.Body;
        rawBody.Should().NotBeNullOrEmpty("request must have a JSON body");
        var body = JsonDocument.Parse(rawBody!).RootElement;

        body.GetProperty("project").GetString().Should().Be("billing");
        body.GetProperty("template").GetString().Should().Be("invoice");
        body.GetProperty("version").GetString().Should().Be("2.0.0");
        body.GetProperty("data").GetProperty("customerName").GetString().Should().Be("Acme Corp");
    }

    // ------------------------------------------------------------------ //
    // 10. Guard — null input
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_throws_ArgumentNullException_when_input_is_null()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        var act = async () => await client.Render.PdfAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("input");
    }

    // ------------------------------------------------------------------ //
    // 11a. Pre-cancelled token short-circuits before SendAsync
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_throws_when_token_pre_cancelled()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await client.Render.PdfAsync(DefaultInput(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ------------------------------------------------------------------ //
    // 11b. Cancellation mid-send aborts the request
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_throws_when_token_cancelled_mid_send()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;

        // Server stub holds the response for 2s so cancellation fires during the send.
        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/pdf")
                  .WithBody(PdfMagicBytes)
                  .WithDelay(TimeSpan.FromSeconds(2)));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = async () => await client.Render.PdfAsync(DefaultInput(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        cts.Token.IsCancellationRequested.Should().BeTrue();
    }

    // ------------------------------------------------------------------ //
    // 12. Per-call extra headers forwarded
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_passes_per_call_Headers()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        await client.Render.PdfAsync(
            DefaultInput(),
            new RequestOptions
            {
                Headers = new Dictionary<string, string> { ["X-Trace-Id"] = "abc" },
            });

        var entry = PostEntry(server);
        var value = GetSingleHeader(entry.RequestMessage.Headers!, "X-Trace-Id");
        value.Should().Be("abc");
    }

    // ------------------------------------------------------------------ //
    // 13. Absolute URI from BaseAddress — not from caller HttpClient.BaseAddress
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_uses_absolute_URI_built_from_BaseAddress_not_caller_HttpClient_BaseAddress()
    {
        // Arrange: WireMock server is the real SDK base address.
        using var server = WireMockServer.Start();
        StubRender(server);

        // A caller-owned HttpClient with a DIFFERENT BaseAddress (a non-existent host).
        using var callerHttpClient = new HttpClient
        {
            BaseAddress = new Uri("http://should-never-be-used.invalid"),
        };

        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test_unit",
            BaseUrl = new Uri(server.Url!),   // SDK will use this
            HttpClient = callerHttpClient,      // BaseAddress on this must be ignored
        });

        // Act
        var pdf = await client.Render.PdfAsync(DefaultInput());

        // Assert: WireMock received the POST (SDK used the correct base address).
        // PdfAsync issues POST /v1/render + GET /storage/... so two entries are expected;
        // we filter to the POST as the signal that the API base address was honored.
        pdf[..4].Should().Equal(0x25, 0x50, 0x44, 0x46); // %PDF
        PostEntry(server).RequestMessage.Path.Should().Be("/v1/render");
    }

    // ------------------------------------------------------------------ //
    // 14. Base URL with a path prefix (e.g. reverse-proxy mount) is preserved
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_preserves_base_path_prefix_when_BaseUrl_includes_a_path()
    {
        // Why: `new Uri(base, "/v1/render")` silently drops base path segments because the
        // leading '/' makes the relative absolute (RFC 3986 §5.2). HttpTransport.ComposeUri
        // must normalise so a base URL like `https://proxy/api/` correctly composes to
        // `https://proxy/api/v1/render` rather than `https://proxy/v1/render`.
        using var server = WireMockServer.Start();
        var presignedUrl = $"{server.Url}/api/storage/doc_abc123.pdf";
        var descriptorJson = $$"""
            {
                "documentId": "doc_abc123",
                "organizationId": "org_xyz",
                "environment": "test",
                "format": "pdf",
                "pageCount": 1,
                "sizeBytes": 100,
                "presignedPdfUrl": "{{presignedUrl}}"
            }
            """;
        server.Given(Request.Create().WithPath("/api/v1/render").UsingPost())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(descriptorJson));
        server.Given(Request.Create().WithPath("/api/storage/doc_abc123.pdf").UsingGet())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/pdf")
                  .WithBody(PdfMagicBytes));

        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test_unit",
            BaseUrl = new Uri(server.Url + "/api/"),
        });

        var pdf = await client.Render.PdfAsync(DefaultInput());

        pdf[..4].Should().Equal(0x25, 0x50, 0x44, 0x46);
        PostEntry(server).RequestMessage.Path.Should().Be("/api/v1/render");
    }

    // ------------------------------------------------------------------ //
    // 15. PdfStreamAsync returns a Stream that owns the response
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfStreamAsync_returns_streamed_PDF_bytes()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        using var stream = await client.Render.PdfStreamAsync(DefaultInput());

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        var bytes = buffer.ToArray();

        bytes.Should().NotBeEmpty();
        bytes[..4].Should().Equal(0x25, 0x50, 0x44, 0x46);
    }

    [Fact]
    public async Task PdfStreamAsync_disposing_stream_marks_it_closed()
    {
        // The wrapper Stream owns the HttpResponseMessage. After Dispose, the
        // wrapper's CanRead returns false — this is the public, observable proof
        // that disposal happened. We can't see the response object being disposed
        // from the outside, but the CanRead flag is the contract.
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        var stream = await client.Render.PdfStreamAsync(DefaultInput());
        stream.CanRead.Should().BeTrue("a fresh stream must be readable");

        stream.Dispose();

        stream.CanRead.Should().BeFalse("a disposed stream must not advertise readability");
    }

    [Fact]
    public async Task PdfStreamAsync_propagates_HTTP_error_status_as_PoliPageException()
    {
        using var harness = StartServerAndClient();
        var (server, _) = harness;
        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(Response.Create()
                  .WithStatusCode(401)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody("{\"code\":\"UNAUTHORIZED\",\"message\":\"bad key\"}"));

        // MaxRetries=0 keeps the test fast — 401 isn't retried anyway, but explicit
        // is better than relying on it.
        using var clientWithoutRetry = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test_unit",
            BaseUrl = new Uri(server.Url!),
            MaxRetries = 0,
        });

        var act = async () =>
        {
            using var _ = await clientWithoutRetry.Render.PdfStreamAsync(DefaultInput());
        };

        await act.Should().ThrowAsync<PoliPageAuthException>();
    }

    [Fact]
    public async Task PdfStreamAsync_throws_ArgumentNullException_when_input_is_null()
    {
        using var harness = StartServerAndClient();
        var (_, client) = harness;

        var act = async () => await client.Render.PdfStreamAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("input");
    }

    // ------------------------------------------------------------------ //
    // 19. DocumentAsync returns a descriptor with the presigned URL
    // ------------------------------------------------------------------ //

    private const string SampleDescriptorJson = """
        {
            "documentId": "doc_abc123",
            "organizationId": "org_xyz",
            "projectSlug": "billing",
            "templateSlug": "invoice",
            "version": "1.0.0",
            "environment": "test",
            "format": "pdf",
            "pageCount": 3,
            "sizeBytes": 12345,
            "presignedPdfUrl": "https://placeholder.invalid/doc_abc123.pdf"
        }
        """;

    [Fact]
    public async Task DocumentAsync_returns_descriptor_from_JSON_response()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(SampleDescriptorJson));

        var descriptor = await client.Render.DocumentAsync(DefaultInput());

        descriptor.DocumentId.Should().Be("doc_abc123");
        descriptor.OrganizationId.Should().Be("org_xyz");
        descriptor.PageCount.Should().Be(3);
        descriptor.SizeBytes.Should().Be(12345);
        descriptor.PresignedPdfUrl.Should().Be("https://placeholder.invalid/doc_abc123.pdf");
        descriptor.Format.Should().Be("pdf");
    }

    [Fact]
    public async Task DocumentAsync_sends_Accept_application_json_not_pdf()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(SampleDescriptorJson));

        await client.Render.DocumentAsync(DefaultInput());

        var entry = PostEntry(server);
        entry.RequestMessage.Headers!["Accept"].Should().Contain("application/json");
        entry.RequestMessage.Headers["Accept"].Should().NotContain("application/pdf");
    }

    [Fact]
    public async Task DocumentAsync_throws_ArgumentNullException_when_input_is_null()
    {
        using var harness = StartServerAndClient();
        var (_, client) = harness;

        var act = async () => await client.Render.DocumentAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("input");
    }

    // ------------------------------------------------------------------ //
    // 22. DocumentDescriptor.DownloadPdfAsync uses the SDK-injected downloader
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task DownloadPdfAsync_fetches_bytes_from_presigned_URL()
    {
        // Stub the API to return a descriptor whose PresignedPdfUrl points at WireMock.
        using var server = WireMockServer.Start();
        var presignedUrl = $"{server.Url}/storage/doc_abc.pdf";
        var descriptorJson = SampleDescriptorJson.Replace(
            "https://placeholder.invalid/doc_abc123.pdf", presignedUrl, StringComparison.Ordinal);

        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(descriptorJson));

        server.Given(Request.Create().WithPath("/storage/doc_abc.pdf").UsingGet())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/pdf")
                  .WithBody(PdfMagicBytes));

        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test_unit",
            BaseUrl = new Uri(server.Url!),
        });

        var descriptor = await client.Render.DocumentAsync(DefaultInput());
        var pdf = await descriptor.DownloadPdfAsync();

        pdf[..4].Should().Equal(0x25, 0x50, 0x44, 0x46);
    }

    [Fact]
    public async Task DownloadPdfAsync_does_NOT_send_Authorization_to_presigned_URL()
    {
        // The header-less download transport must never leak the API auth onto S3 — S3
        // would reject the request as a signature mismatch. Verify by inspecting the
        // captured request to the storage path.
        using var server = WireMockServer.Start();
        var presignedUrl = $"{server.Url}/storage/doc_abc.pdf";
        var descriptorJson = SampleDescriptorJson.Replace(
            "https://placeholder.invalid/doc_abc123.pdf", presignedUrl, StringComparison.Ordinal);

        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(descriptorJson));

        server.Given(Request.Create().WithPath("/storage/doc_abc.pdf").UsingGet())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/pdf")
                  .WithBody(PdfMagicBytes));

        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test_unit",
            BaseUrl = new Uri(server.Url!),
        });

        var descriptor = await client.Render.DocumentAsync(DefaultInput());
        await descriptor.DownloadPdfAsync();

        var downloadEntry = server.LogEntries.Should().Contain(e =>
            e.RequestMessage.Path == "/storage/doc_abc.pdf").Subject;
        downloadEntry.RequestMessage.Headers.Should().NotContainKey("Authorization",
            "the download transport must never carry the SDK's API auth header");
    }

    [Fact]
    public void DownloadPdfAsync_throws_when_descriptor_has_no_downloader()
    {
        // Constructed manually — Downloader is null — DownloadPdfAsync must refuse.
        var descriptor = new DocumentDescriptor
        {
            DocumentId = "doc_manual",
            OrganizationId = "org_manual",
            Environment = "test",
            Format = "pdf",
            PageCount = 1,
            SizeBytes = 100,
            PresignedPdfUrl = "https://example.invalid/doc.pdf",
        };

        var act = async () => await descriptor.DownloadPdfAsync();

        act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*was not produced by a PoliPageClient*");
    }

    // ------------------------------------------------------------------ //
    // 26. PreviewAsync — HTML preview, accepts both modes
    // ------------------------------------------------------------------ //

    private const string SamplePreviewJson = """
        {
            "html": "<html><body>Page 1</body><body>Page 2</body></html>",
            "totalPages": 2,
            "environment": "sandbox"
        }
        """;

    [Fact]
    public async Task PreviewAsync_returns_PreviewResult_from_JSON()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        server.Given(Request.Create().WithPath("/v1/render/preview").UsingPost())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(SamplePreviewJson));

        var result = await client.Render.PreviewAsync(DefaultInput());

        result.Html.Should().Contain("Page 1");
        result.Html.Should().Contain("Page 2");
        result.TotalPages.Should().Be(2);
        result.Environment.Should().Be("sandbox");
    }

    [Fact]
    public async Task PreviewAsync_accepts_InlineModeInput()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        server.Given(Request.Create().WithPath("/v1/render/preview").UsingPost())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(SamplePreviewJson));

        // The point of taking RenderInput as the abstract base: this compiles.
        var inline = new InlineModeInput { Template = "<html><body>Hi</body></html>" };
        var result = await client.Render.PreviewAsync(inline);

        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task PreviewAsync_posts_to_preview_endpoint_with_Accept_json()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        server.Given(Request.Create().WithPath("/v1/render/preview").UsingPost())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(SamplePreviewJson));

        await client.Render.PreviewAsync(DefaultInput());

        var entry = PostEntry(server);
        entry.RequestMessage.Path.Should().Be("/v1/render/preview");
        entry.RequestMessage.Headers!["Accept"].Should().Contain("application/json");
    }

    [Fact]
    public async Task PreviewAsync_throws_ArgumentNullException_when_input_is_null()
    {
        using var harness = StartServerAndClient();
        var (_, client) = harness;

        var act = async () => await client.Render.PreviewAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("input");
    }

    // ------------------------------------------------------------------ //
    // RenderToFileAsync convenience helper
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task RenderToFileAsync_writes_PDF_bytes_to_disk()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        var tempPath = Path.Combine(Path.GetTempPath(), $"polipage-sdk-test-{Guid.NewGuid():N}.pdf");
        try
        {
            await client.RenderToFileAsync(DefaultInput(), tempPath);

            File.Exists(tempPath).Should().BeTrue();
            var written = await File.ReadAllBytesAsync(tempPath);
            written[..4].Should().Equal(0x25, 0x50, 0x44, 0x46);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public async Task RenderToFileAsync_overwrites_existing_file()
    {
        using var harness = StartServerAndClient();
        var (server, client) = harness;
        StubRender(server);

        var tempPath = Path.Combine(Path.GetTempPath(), $"polipage-sdk-test-{Guid.NewGuid():N}.pdf");
        try
        {
            await File.WriteAllBytesAsync(tempPath, new byte[] { 0x00, 0x01, 0x02 });
            await client.RenderToFileAsync(DefaultInput(), tempPath);

            var written = await File.ReadAllBytesAsync(tempPath);
            written[..4].Should().Equal(0x25, 0x50, 0x44, 0x46);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RenderToFileAsync_throws_ArgumentException_when_path_is_invalid(string? path)
    {
        using var harness = StartServerAndClient();
        var (_, client) = harness;

        var act = async () => await client.RenderToFileAsync(DefaultInput(), path!);

        var ex = await act.Should().ThrowAsync<ArgumentException>();
        ex.Which.ParamName.Should().Be(nameof(path));
    }

    [Fact]
    public async Task RenderToFileAsync_throws_ArgumentNullException_when_input_is_null()
    {
        using var harness = StartServerAndClient();
        var (_, client) = harness;

        var act = async () => await client.RenderToFileAsync(null!, "/tmp/x.pdf");

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("input");
    }

    [Fact]
    public async Task DownloadPdfAsync_throws_PoliPageDownloadException_on_non_2xx()
    {
        using var server = WireMockServer.Start();
        var presignedUrl = $"{server.Url}/storage/expired.pdf";
        var descriptorJson = SampleDescriptorJson.Replace(
            "https://placeholder.invalid/doc_abc123.pdf", presignedUrl, StringComparison.Ordinal);

        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(Response.Create()
                  .WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(descriptorJson));

        // The presigned URL has expired — S3 typically responds with 403.
        server.Given(Request.Create().WithPath("/storage/expired.pdf").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(403));

        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test_unit",
            BaseUrl = new Uri(server.Url!),
        });

        var descriptor = await client.Render.DocumentAsync(DefaultInput());

        var act = async () => await descriptor.DownloadPdfAsync();

        var ex = await act.Should().ThrowAsync<PoliPageDownloadException>();
        ex.Which.StatusCode.Should().Be(403);
        ex.Which.Code.Should().Be(PoliPageErrorCode.DownloadFailed);
    }
}
