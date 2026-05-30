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
            ApiKey = "pp_test_unit",
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
            "version": "1.0.0",
            "environment": "test",
            "format": "pdf",
            "pageCount": 3,
            "sizeBytes": 12345,
            "presignedPdfUrl": "https://placeholder.invalid/doc_abc123.pdf"
        }
        """;

    // ------------------------------------------------------------------ //
    // GetAsync
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task GetAsync_returns_DocumentDescriptor_for_existing_document()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/documents/doc_abc123").UsingGet())
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
        harness.Server.Given(Request.Create().WithPath("/documents/doc_abc123").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(200)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody(SampleDescriptorJson));

        await harness.Client.Documents.GetAsync("doc_abc123");

        var entry = harness.Server.LogEntries.Should().ContainSingle().Subject;
        entry.RequestMessage.Method.Should().Be("GET");
        entry.RequestMessage.Path.Should().Be("/documents/doc_abc123");
        entry.RequestMessage.Headers!["Authorization"].Should().Contain("Bearer pp_test_unit");
        entry.RequestMessage.Headers["Accept"].Should().Contain("application/json");
    }

    [Fact]
    public async Task GetAsync_descriptor_carries_working_Downloader()
    {
        // Round-trip: GetAsync → DownloadPdfAsync via the same WireMock instance.
        using var harness = StartHarness();
        var presignedUrl = $"{harness.Server.Url}/storage/doc_abc.pdf";
        var descriptorJson = SampleDescriptorJson.Replace(
            "https://placeholder.invalid/doc_abc123.pdf", presignedUrl, StringComparison.Ordinal);

        harness.Server.Given(Request.Create().WithPath("/documents/doc_abc123").UsingGet())
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
        harness.Server.Given(Request.Create().WithPath("/documents/doc_missing").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(404)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody("""{"code":"DOCUMENT_NOT_FOUND","message":"no such document"}"""));

        var act = async () => await harness.Client.Documents.GetAsync("doc_missing");

        var ex = await act.Should().ThrowAsync<PoliPageNotFoundException>();
        ex.Which.Code.Should().Be("DOCUMENT_NOT_FOUND");
    }

    [Fact]
    public async Task GetAsync_propagates_410_as_PoliPageGoneException()
    {
        using var harness = StartHarness();
        harness.Server.Given(Request.Create().WithPath("/documents/doc_deleted").UsingGet())
               .RespondWith(Response.Create()
                   .WithStatusCode(410)
                   .WithHeader("Content-Type", "application/json")
                   .WithBody("""{"code":"GONE","message":"soft-deleted"}"""));

        var act = async () => await harness.Client.Documents.GetAsync("doc_deleted");

        await act.Should().ThrowAsync<PoliPageGoneException>();
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

}
