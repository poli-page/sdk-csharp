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

    private static (WireMockServer Server, PoliPageClient Client) StartServerAndClient(string apiKey = "pp_test_unit")
    {
        var server = WireMockServer.Start();
        var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = apiKey,
            BaseUrl = new Uri(server.Url!),
        });
        return (server, client);
    }

    /// <summary>Registers a default POST /render stub that returns a %PDF-1.4 body.</summary>
    private static void StubRender(WireMockServer server, byte[]? body = null)
    {
        server.Given(
            Request.Create().WithPath("/render").UsingPost())
        .RespondWith(
            Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/pdf")
                .WithBody(body ?? PdfMagicBytes));
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
        var (server, client) = StartServerAndClient();
        using var c = client;
        StubRender(server);

        var pdf = await c.Render.PdfAsync(DefaultInput());

        pdf.Should().NotBeEmpty();
        pdf[..4].Should().Equal(0x25, 0x50, 0x44, 0x46); // %PDF
    }

    // ------------------------------------------------------------------ //
    // 2. Method + path
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_sends_POST_to_render()
    {
        var (server, client) = StartServerAndClient();
        using var c = client;
        StubRender(server);

        await c.Render.PdfAsync(DefaultInput());

        var entry = server.LogEntries.Should().ContainSingle().Subject;
        entry.RequestMessage.Method.Should().Be("POST");
        entry.RequestMessage.Path.Should().Be("/render");
    }

    // ------------------------------------------------------------------ //
    // 3. Authorization header
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_sends_Authorization_Bearer_header()
    {
        var (server, client) = StartServerAndClient(apiKey: "pp_test_unit");
        using var c = client;
        StubRender(server);

        await c.Render.PdfAsync(DefaultInput());

        var entry = server.LogEntries.Should().ContainSingle().Subject;
        var value = GetSingleHeader(entry.RequestMessage.Headers!, "Authorization");
        value.Should().Be("Bearer pp_test_unit");
    }

    // ------------------------------------------------------------------ //
    // 4. User-Agent header
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_sends_User_Agent_header()
    {
        var (server, client) = StartServerAndClient();
        using var c = client;
        StubRender(server);

        await c.Render.PdfAsync(DefaultInput());

        var entry = server.LogEntries.Should().ContainSingle().Subject;
        var ua = GetSingleHeader(entry.RequestMessage.Headers!, "User-Agent");
        ua.Should().StartWith("poli-page-sdk-dotnet/");
    }

    // ------------------------------------------------------------------ //
    // 5. Accept: application/pdf
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_sends_Accept_application_pdf()
    {
        var (server, client) = StartServerAndClient();
        using var c = client;
        StubRender(server);

        await c.Render.PdfAsync(DefaultInput());

        var entry = server.LogEntries.Should().ContainSingle().Subject;
        entry.RequestMessage.Headers.Should().ContainKey("Accept");
        entry.RequestMessage.Headers!["Accept"].Should().Contain("application/pdf");
    }

    // ------------------------------------------------------------------ //
    // 6. Content-Type: application/json
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_sends_Content_Type_application_json()
    {
        var (server, client) = StartServerAndClient();
        using var c = client;
        StubRender(server);

        await c.Render.PdfAsync(DefaultInput());

        var entry = server.LogEntries.Should().ContainSingle().Subject;
        var ct = GetSingleHeader(entry.RequestMessage.Headers!, "Content-Type");
        ct.Should().StartWith("application/json");
    }

    // ------------------------------------------------------------------ //
    // 7. Auto-generated Idempotency-Key is a valid GUID
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_sends_auto_generated_Idempotency_Key_when_not_provided()
    {
        var (server, client) = StartServerAndClient();
        using var c = client;
        StubRender(server);

        await c.Render.PdfAsync(DefaultInput());

        var entry = server.LogEntries.Should().ContainSingle().Subject;
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
        var (server, client) = StartServerAndClient();
        using var c = client;
        StubRender(server);

        await c.Render.PdfAsync(DefaultInput(), new RequestOptions { IdempotencyKey = "inv-INV-001" });

        var entry = server.LogEntries.Should().ContainSingle().Subject;
        var key = GetSingleHeader(entry.RequestMessage.Headers!, "Idempotency-Key");
        key.Should().Be("inv-INV-001");
    }

    // ------------------------------------------------------------------ //
    // 9. Body serialized as camelCase JSON
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_serializes_body_as_camelCase_JSON()
    {
        var (server, client) = StartServerAndClient();
        using var c = client;
        StubRender(server);

        var input = new ProjectModeInput
        {
            Project = "billing",
            Template = "invoice",
            Version = "2.0.0",
            Data = new { customerName = "Acme Corp" },
        };

        await c.Render.PdfAsync(input);

        var entry = server.LogEntries.Should().ContainSingle().Subject;
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
        var (server, client) = StartServerAndClient();
        using var c = client;
        StubRender(server);

        var act = async () => await c.Render.PdfAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("input");
    }

    // ------------------------------------------------------------------ //
    // 11. CancellationToken cancels the operation
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_passes_cancellationToken()
    {
        var (server, client) = StartServerAndClient();
        using var c = client;
        StubRender(server);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await c.Render.PdfAsync(DefaultInput(), cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // Request was cancelled before or during send; WireMock may or may not have received it.
        // The important guarantee: no successful return value was produced.
    }

    // ------------------------------------------------------------------ //
    // 12. Per-call extra headers forwarded
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PdfAsync_passes_per_call_Headers()
    {
        var (server, client) = StartServerAndClient();
        using var c = client;
        StubRender(server);

        await c.Render.PdfAsync(
            DefaultInput(),
            new RequestOptions
            {
                Headers = new Dictionary<string, string> { ["X-Trace-Id"] = "abc" },
            });

        var entry = server.LogEntries.Should().ContainSingle().Subject;
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
        var server = WireMockServer.Start();
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

        // Assert: WireMock received the request (SDK used the correct base address)
        pdf[..4].Should().Equal(0x25, 0x50, 0x44, 0x46); // %PDF
        server.LogEntries.Should().ContainSingle("the request should have been routed to the SDK base URL");
    }
}
