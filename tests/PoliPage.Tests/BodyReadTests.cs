using System.Text;
using FluentAssertions;
using PoliPage.Tests.Contract;
using Xunit;
using static PoliPage.Tests.TransportTestSupport;

namespace PoliPage.Tests;

/// <summary>
/// An API answer's whole body is read inside its attempt (spec §9.1, §8.2), and a failure
/// while reading a PDF is the SDK's own error, never the HTTP stack's.
/// </summary>
public sealed class BodyReadTests
{
    private static readonly KeyValuePair<string, string> RequestId = new("X-Request-Id", "req_cut");

    [Fact]
    public async Task A_2xx_cut_mid_body_is_a_network_error_and_is_retried()
    {
        await using var server = new MockServer(origin => [Descriptor(origin).Cut(), Descriptor(origin)]);
        var responses = new List<ResponseEvent>();
        var retries = new List<RetryEvent>();
        using var client = new PoliPageClient(Options(server, maxRetries: 1) with
        {
            OnResponse = responses.Add,
            OnRetry = retries.Add,
        });

        var document = await client.Documents.GetAsync("doc_abc123");

        document.DocumentId.Should().Be("doc_abc123");
        server.Requests.Should().HaveCount(2);
        retries.Should().ContainSingle().Which.Reason.Should().Be("Network request failed");
        responses.Should().ContainSingle("OnResponse fires once the body is read, never for the cut answer");
    }

    [Fact]
    public async Task A_2xx_cut_mid_body_without_retries_raises_network_error_with_the_read_error()
    {
        await using var server = new MockServer(origin => [Descriptor(origin).Cut()]);
        using var client = new PoliPageClient(Options(server));

        var act = () => client.Render.DocumentAsync(Input);

        var thrown = await act.Should().ThrowAsync<PoliPageNetworkException>();
        thrown.Which.Message.Should().Be("Network request failed");
        thrown.Which.InnerException.Should().NotBeNull();
    }

    [Fact]
    public async Task An_error_answer_cut_mid_body_keeps_its_status_and_is_retried_by_it()
    {
        var unavailable = JsonAnswer(503, """{"error":"PUBLISH_IN_PROGRESS","detail":"busy"}""", RequestId).Cut();
        await using var server = new MockServer(_ => [unavailable, unavailable]);
        using var client = new PoliPageClient(Options(server, maxRetries: 1));

        var act = () => client.Documents.GetAsync("doc_abc123");

        var thrown = await act.Should().ThrowAsync<PoliPageException>();
        thrown.Which.Code.Should().Be(PoliPageErrorCode.InternalError, "never a code from the partial bytes");
        thrown.Which.Message.Should().Be("HTTP 503");
        thrown.Which.StatusCode.Should().Be(503);
        thrown.Which.RequestId.Should().Be("req_cut");
        thrown.Which.InnerException.Should().NotBeNull();
        server.Requests.Should().HaveCount(2, "a 503 is retried");
    }

    [Fact]
    public async Task A_400_cut_mid_body_is_not_retried()
    {
        var invalid = JsonAnswer(400, """{"error":"VALIDATION_ERROR","detail":"bad"}""").Cut();
        await using var server = new MockServer(_ => [invalid, invalid]);
        using var client = new PoliPageClient(Options(server, maxRetries: 2));

        var act = () => client.Documents.GetAsync("doc_abc123");

        var thrown = await act.Should().ThrowAsync<PoliPageValidationException>();
        thrown.Which.Code.Should().Be(PoliPageErrorCode.InternalError);
        thrown.Which.Message.Should().Be("HTTP 400");
        server.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task A_storage_503_is_DOWNLOAD_FAILED_never_retried()
    {
        await using var server = new MockServer(origin =>
            [Descriptor(origin), new MockStep(503, [new("Content-Type", "application/xml")], Encoding.UTF8.GetBytes("<Error/>"), null)]);
        using var client = new PoliPageClient(Options(server, maxRetries: 2));

        var act = () => client.Render.PdfAsync(Input);

        var thrown = await act.Should().ThrowAsync<PoliPageDownloadException>();
        thrown.Which.Code.Should().Be(PoliPageErrorCode.DownloadFailed);
        thrown.Which.Message.Should().Be("Failed to download PDF: HTTP 503");
        thrown.Which.StatusCode.Should().Be(503);
        thrown.Which.IsRetryable().Should().BeFalse();
        server.Requests.Should().HaveCount(2, "the render, then one download");
    }

    [Fact]
    public async Task A_pdf_cut_mid_body_is_DOWNLOAD_FAILED()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), Bytes(200, Pdf, "application/pdf").Cut()]);
        using var client = new PoliPageClient(Options(server));

        var act = () => client.Render.PdfAsync(Input);

        var thrown = await act.Should().ThrowAsync<PoliPageDownloadException>();
        thrown.Which.Message.Should().Be("Failed to download PDF: network error");
        thrown.Which.StatusCode.Should().Be(0);
        thrown.Which.InnerException.Should().NotBeNull();
    }

    [Fact]
    public async Task A_pdf_stream_cut_mid_body_raises_DOWNLOAD_FAILED_from_its_reads()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), Bytes(200, Pdf, "application/pdf").Cut()]);
        using var client = new PoliPageClient(Options(server));

        var stream = await client.Render.PdfStreamAsync(Input);
        await using (stream)
        {
            var act = () => stream.CopyToAsync(Stream.Null);

            var thrown = await act.Should().ThrowAsync<PoliPageDownloadException>();
            thrown.Which.Message.Should().Be("Failed to download PDF: network error");
        }
    }

    [Fact]
    public async Task A_pdf_stream_read_cancelled_raises_aborted()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), Bytes(200, Pdf, "application/pdf").Drip()]);
        using var client = new PoliPageClient(Options(server));
        using var cts = new CancellationTokenSource();

        var stream = await client.Render.PdfStreamAsync(Input, cancellationToken: cts.Token);
        await using (stream)
        {
            cts.CancelAfter(TimeSpan.FromMilliseconds(100));
            var act = () => stream.CopyToAsync(Stream.Null, cts.Token);

            var thrown = await act.Should().ThrowAsync<PoliPageException>();
            thrown.Which.Code.Should().Be(PoliPageErrorCode.Aborted);
            thrown.Which.Message.Should().Be("Request was aborted");
            thrown.Which.IsRetryable().Should().BeFalse();
        }
    }

    [Fact]
    public async Task A_download_cancelled_mid_body_raises_aborted()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), Bytes(200, Pdf, "application/pdf").Drip()]);
        using var client = new PoliPageClient(Options(server));
        var document = await client.Render.DocumentAsync(Input);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => document.DownloadPdfAsync(cts.Token);

        (await act.Should().ThrowAsync<PoliPageException>()).Which.Code.Should().Be(PoliPageErrorCode.Aborted);
    }

    [Fact]
    public async Task A_download_client_timeout_is_DOWNLOAD_FAILED()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), MockStep.Hang]);
        using var download = new HttpClient { Timeout = TimeSpan.FromMilliseconds(200) };
        using var client = new PoliPageClient(Options(server) with { DownloadHttpClient = download });

        var act = () => client.Render.PdfAsync(Input);

        var thrown = await act.Should().ThrowAsync<PoliPageDownloadException>();
        thrown.Which.Message.Should().Be("Failed to download PDF: network error");
        thrown.Which.InnerException.Should().BeAssignableTo<OperationCanceledException>();
    }
}
