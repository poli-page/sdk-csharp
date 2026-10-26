using System.Text.Json;
using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace PoliPage.Tests;

public sealed class DocumentsTests
{
    private static readonly byte[] PdfMagicBytes = [0x25, 0x50, 0x44, 0x46];

    private sealed record TestHarness(WireMockServer Server, PoliPageClient Client) : IDisposable
    {
        public void Dispose()
        {
            Client.Dispose();
            Server.Dispose();
        }
    }

    private static TestHarness StartHarness()
    {
        var server = WireMockServer.Start();
        var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_sandbox_unit",
            BaseUrl = new Uri(server.Url!),
            MaxRetries = 0, // these tests don't exercise retry — keep them fast
        });
        return new TestHarness(server, client);
    }

    private const string SampleDescriptorJson = """
        {
            "documentId": "doc_abc123",
            "organizationId": "org_xyz",
            "projectSlug": "billing",
            "templateSlug": "invoice",
            "environment": "sandbox",
            "format": "pdf",
            "pageCount": 3,
            "sizeBytes": 12345,
            "createdAt": "2026-06-03T10:00:00Z",
            "presignedPdfUrl": "https://placeholder.invalid/doc_abc123.pdf",
            "expiresAt": "2026-06-03T10:15:00Z"
        }
        """;

    // ------------------------------------------------------------------ //
    // GetAsync
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task GetAsync_returns_DocumentDescriptor_for_existing_document()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_abc123").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(SampleDescriptorJson));

        var descriptor = await harness.Client.Documents.GetAsync("doc_abc123");

        descriptor.DocumentId.Should().Be("doc_abc123");
        descriptor.PageCount.Should().Be(3);
        descriptor.PresignedPdfUrl.Should().Be("https://placeholder.invalid/doc_abc123.pdf");
    }

    [Fact]
    public async Task GetAsync_sends_GET_with_Authorization_Bearer()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_abc123").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(SampleDescriptorJson));

        await harness.Client.Documents.GetAsync("doc_abc123");

        var entry = harness.Server.LogEntries.Should().ContainSingle().Subject;
        entry.RequestMessage!.Method.Should().Be("GET");
        entry.RequestMessage!.Path.Should().Be("/v1/documents/doc_abc123");
        entry.RequestMessage!.Headers!["Authorization"].Should().Contain("Bearer pp_sandbox_unit");
        entry.RequestMessage!.Headers["Accept"].Should().Contain("application/json");
    }

    [Fact]
    public async Task GetAsync_descriptor_carries_working_Downloader()
    {
        // Round-trip: GetAsync → DownloadPdfAsync via the same WireMock instance.
        using var harness = StartHarness();
        var presignedUrl = $"{harness.Server.Url}/storage/doc_abc.pdf";
        var descriptorJson = SampleDescriptorJson.Replace(
            "https://placeholder.invalid/doc_abc123.pdf", presignedUrl, StringComparison.Ordinal);

        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_abc123").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(descriptorJson));

        harness.Server.Given(Request.Create().WithPath("/storage/doc_abc.pdf").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/pdf")
                   .WithBody(PdfMagicBytes));

        var descriptor = await harness.Client.Documents.GetAsync("doc_abc123");
        var pdf = await descriptor.DownloadPdfAsync();

        pdf[..4].Should().Equal(0x25, 0x50, 0x44, 0x46);
    }

    [Fact]
    public async Task GetAsync_propagates_404_as_PoliPageNotFoundException()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_missing").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(404)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody("""{"error":"DOCUMENT_NOT_FOUND","detail":"no such document"}"""));

        var act = async () => await harness.Client.Documents.GetAsync("doc_missing");

        var ex = await act.Should().ThrowAsync<PoliPageNotFoundException>();
        ex.Which.Code.Should().Be("DOCUMENT_NOT_FOUND");
    }

    [Fact]
    public async Task GetAsync_propagates_410_as_PoliPageGoneException()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_deleted").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(410)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody("""{"error":"DOCUMENT_GONE","detail":"soft-deleted"}"""));

        var act = async () => await harness.Client.Documents.GetAsync("doc_deleted");

        var ex = await act.Should().ThrowAsync<PoliPageGoneException>();
        ex.Which.Code.Should().Be(PoliPageErrorCode.DocumentGone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetAsync_throws_ArgumentException_on_empty_id(string? id)
    {
        using var harness = StartHarness();

        var act = async () => await harness.Client.Documents.GetAsync(id!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("documentId");
    }

    // ------------------------------------------------------------------ //
    // DeleteAsync
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task DeleteAsync_sends_DELETE_to_documents_id()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_abc123").UsingDelete())
               .RespondWith(Response.Create().WithStatusCode(204));

        await harness.Client.Documents.DeleteAsync("doc_abc123");

        var entry = harness.Server.LogEntries.Should().ContainSingle().Subject;
        entry.RequestMessage!.Method.Should().Be("DELETE");
        entry.RequestMessage!.Path.Should().Be("/v1/documents/doc_abc123");
    }

    [Fact]
    public async Task DeleteAsync_returns_void_on_204()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_abc123").UsingDelete())
               .RespondWith(Response.Create().WithStatusCode(204));

        var act = async () => await harness.Client.Documents.DeleteAsync("doc_abc123");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsync_is_idempotent_a_second_delete_resolves_on_204()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_abc123").UsingDelete())
               .RespondWith(Response.Create().WithStatusCode(204));

        await harness.Client.Documents.DeleteAsync("doc_abc123");
        var act = async () => await harness.Client.Documents.DeleteAsync("doc_abc123");

        await act.Should().NotThrowAsync("the API answers 204 for an already-deleted document too");
        harness.Server.LogEntries.Should().HaveCount(2);
    }

    [Fact]
    public async Task DeleteAsync_throws_PoliPageNotFoundException_on_404()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_missing").UsingDelete())
               .RespondWith(Response.Create()
                   .WithStatusCode(404)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody("""{"error":"NOT_FOUND","detail":"no such document"}"""));

        var act = async () => await harness.Client.Documents.DeleteAsync("doc_missing");

        await act.Should().ThrowAsync<PoliPageNotFoundException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteAsync_throws_ArgumentException_on_empty_id(string? id)
    {
        using var harness = StartHarness();

        var act = async () => await harness.Client.Documents.DeleteAsync(id!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("documentId");
    }

    // ------------------------------------------------------------------ //
    // PreviewAsync
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task PreviewAsync_returns_HTML_body_and_PageCount_header()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_abc123/preview").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "text/html")
                   .WithHeader("X-Document-Page-Count", "7")
                   .WithBody("<html><body>preview body</body></html>"));

        var result = await harness.Client.Documents.PreviewAsync("doc_abc123");

        result.Html.Should().Be("<html><body>preview body</body></html>");
        result.PageCount.Should().Be(7);
    }

    [Fact]
    public async Task PreviewAsync_returns_zero_PageCount_when_header_missing()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_abc123/preview").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "text/html")
                   .WithBody("<html></html>"));

        var result = await harness.Client.Documents.PreviewAsync("doc_abc123");

        result.PageCount.Should().Be(0, "missing X-Document-Page-Count must not throw");
    }

    [Fact]
    public async Task PreviewAsync_returns_zero_PageCount_when_header_malformed()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_abc123/preview").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "text/html")
                   .WithHeader("X-Document-Page-Count", "not-a-number")
                   .WithBody("<html></html>"));

        var result = await harness.Client.Documents.PreviewAsync("doc_abc123");

        result.PageCount.Should().Be(0, "malformed header value must not throw");
    }

    [Fact]
    public async Task PreviewAsync_sends_Accept_text_html()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_abc123/preview").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "text/html")
                   .WithHeader("X-Document-Page-Count", "1")
                   .WithBody("<html></html>"));

        await harness.Client.Documents.PreviewAsync("doc_abc123");

        var entry = harness.Server.LogEntries.Should().ContainSingle().Subject;
        entry.RequestMessage!.Headers!["Accept"].Should().Contain("text/html");
    }

    [Fact]
    public async Task PreviewAsync_throws_PoliPageNotFoundException_on_404()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_missing/preview").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(404)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody("""{"error":"DOCUMENT_NOT_FOUND","detail":"no such document"}"""));

        var act = async () => await harness.Client.Documents.PreviewAsync("doc_missing");

        await act.Should().ThrowAsync<PoliPageNotFoundException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PreviewAsync_throws_ArgumentException_on_empty_id(string? id)
    {
        using var harness = StartHarness();

        var act = async () => await harness.Client.Documents.PreviewAsync(id!);

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("documentId");
    }

    // ------------------------------------------------------------------ //
    // ThumbnailsAsync
    // ------------------------------------------------------------------ //

    private const string SampleThumbnailsJson = """
        {
            "thumbnails": [
                { "page": 1, "width": 320, "height": 452, "contentType": "image/png", "data": "iVBOR..." },
                { "page": 2, "width": 320, "height": 452, "contentType": "image/png", "data": "iVBOR..." }
            ]
        }
        """;

    [Fact]
    public async Task ThumbnailsAsync_returns_Thumbnail_array_from_wire_envelope()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_abc123/thumbnails").UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(SampleThumbnailsJson));

        var thumbs = await harness.Client.Documents.ThumbnailsAsync(
            "doc_abc123",
            new ThumbnailOptions { Width = 320, Format = ThumbnailFormat.Png });

        thumbs.Should().HaveCount(2);
        thumbs[0].Page.Should().Be(1);
        thumbs[0].Width.Should().Be(320);
        thumbs[0].ContentType.Should().Be("image/png");
        thumbs[0].Data.Should().StartWith("iVBOR");
        thumbs[1].Page.Should().Be(2);
    }

    [Fact]
    public async Task ThumbnailsAsync_serializes_options_inside_thumbnails_envelope_with_lowercase_format()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_abc123/thumbnails").UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(SampleThumbnailsJson));

        await harness.Client.Documents.ThumbnailsAsync(
            "doc_abc123",
            new ThumbnailOptions { Width = 320, Format = ThumbnailFormat.Jpeg });

        var entry = harness.Server.LogEntries.Should().ContainSingle().Subject;
        var body = JsonDocument.Parse(entry.RequestMessage!.Body!).RootElement;

        // Wire shape: { "thumbnails": { width, format, ... } } — see sdk-node/src/documents.ts:95-99.
        body.TryGetProperty("thumbnails", out var inner).Should().BeTrue(
            "the deployed API expects the options nested under a 'thumbnails' key");
        inner.GetProperty("width").GetInt32().Should().Be(320);
        inner.GetProperty("format").GetString().Should().Be("jpeg",
            "ThumbnailFormat must serialise as a camelCase lowercase string for the wire");
    }

    [Fact]
    public async Task ThumbnailsAsync_returns_empty_list_when_envelope_has_no_thumbnails()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_empty/thumbnails").UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody("{\"thumbnails\":[]}"));

        var thumbs = await harness.Client.Documents.ThumbnailsAsync(
            "doc_empty",
            new ThumbnailOptions());

        thumbs.Should().BeEmpty();
    }

    private static void StubThumbnails(TestHarness harness)
    {
        harness.Server.Given(Request.Create().WithPath("/v1/documents/doc_abc123/thumbnails").UsingPost())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(SampleThumbnailsJson));
    }

    private static string SentBody(TestHarness harness)
        => harness.Server.LogEntries.Should().ContainSingle().Subject.RequestMessage!.Body!;

    [Fact]
    public async Task ThumbnailsAsync_with_no_options_sends_an_empty_object()
    {
        using var harness = StartHarness();
        StubThumbnails(harness);

        var thumbs = await harness.Client.Documents.ThumbnailsAsync("doc_abc123");

        thumbs.Should().HaveCount(2);
        SentBody(harness).Should().Be("{}", "every API default applies when no option is set");
    }

    [Fact]
    public async Task ThumbnailsAsync_with_all_options_unset_sends_an_empty_object()
    {
        using var harness = StartHarness();
        StubThumbnails(harness);

        await harness.Client.Documents.ThumbnailsAsync("doc_abc123", new ThumbnailOptions());

        SentBody(harness).Should().Be("{}");
    }

    [Theory]
    [InlineData("width")]
    [InlineData("format")]
    [InlineData("quality")]
    [InlineData("pages")]
    public async Task ThumbnailsAsync_sends_the_envelope_when_any_single_option_is_set(string option)
    {
        using var harness = StartHarness();
        StubThumbnails(harness);
        var thumbnailOptions = option switch
        {
            "width" => new ThumbnailOptions { Width = 320 },
            "format" => new ThumbnailOptions { Format = ThumbnailFormat.Png },
            "quality" => new ThumbnailOptions { Quality = 80 },
            _ => new ThumbnailOptions { Pages = [2] },
        };

        await harness.Client.Documents.ThumbnailsAsync("doc_abc123", thumbnailOptions);

        var inner = JsonDocument.Parse(SentBody(harness)).RootElement.GetProperty("thumbnails");
        inner.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo([option]);
    }

    [Fact]
    public async Task ThumbnailsAsync_sends_only_the_options_that_are_set()
    {
        using var harness = StartHarness();
        StubThumbnails(harness);

        await harness.Client.Documents.ThumbnailsAsync("doc_abc123", new ThumbnailOptions { Format = ThumbnailFormat.Webp });

        var inner = JsonDocument.Parse(SentBody(harness)).RootElement.GetProperty("thumbnails");
        inner.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(["format"]);
        inner.GetProperty("format").GetString().Should().Be("webp");
    }

    [Fact]
    public async Task ThumbnailsAsync_does_not_check_option_bounds_client_side()
    {
        using var harness = StartHarness();
        StubThumbnails(harness);

        await harness.Client.Documents.ThumbnailsAsync(
            "doc_abc123",
            new ThumbnailOptions { Width = 5000, Format = ThumbnailFormat.Png, Quality = 500 });

        var inner = JsonDocument.Parse(SentBody(harness)).RootElement.GetProperty("thumbnails");
        inner.GetProperty("width").GetInt32().Should().Be(5000, "the API answers 400 VALIDATION_ERROR; the SDK does not pre-check");
        inner.GetProperty("quality").GetInt32().Should().Be(500);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ThumbnailsAsync_throws_ArgumentException_on_empty_id(string? id)
    {
        using var harness = StartHarness();

        var act = async () => await harness.Client.Documents.ThumbnailsAsync(id!, new ThumbnailOptions());

        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("documentId");
    }
}
