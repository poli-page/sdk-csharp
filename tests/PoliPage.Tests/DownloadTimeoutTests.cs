using FluentAssertions;
using PoliPage.Tests.Contract;
using Xunit;
using static PoliPage.Tests.TransportTestSupport;

namespace PoliPage.Tests;

/// <summary>
/// The client's timeout bounds the presigned-URL download: the whole download for
/// <c>Render.PdfAsync</c> and <c>DownloadPdfAsync</c>, the wait for the answer and each read
/// for <c>Render.PdfStreamAsync</c>. Past it: <c>DOWNLOAD_FAILED</c>, no status, the message
/// <c>Failed to download PDF: timeout</c>, never retried.
/// </summary>
/// <remarks>
/// The mock's <c>drip</c> sends one byte every 50 ms, so the test PDF (about 40 bytes) takes
/// about 2 s in all. The timeouts below sit far from both: 500 ms is ten gaps, a quarter of
/// the total, which keeps the tests stable on a slow runner.
/// </remarks>
public sealed class DownloadTimeoutTests
{
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(500);

    private static MockStep PdfAnswer() => Bytes(200, Pdf, "application/pdf");

    private static void ShouldBeDownloadTimeout(PoliPageException error)
    {
        error.Should().BeOfType<PoliPageDownloadException>();
        error.Code.Should().Be(PoliPageErrorCode.DownloadFailed);
        error.Message.Should().Be("Failed to download PDF: timeout");
        error.StatusCode.Should().Be(0);
        error.RequestId.Should().BeNull();
        error.IsRetryable().Should().BeFalse();
        error.IsNetworkError().Should().BeFalse();
    }

    [Fact]
    public async Task Pdf_whose_storage_never_answers_times_out_and_is_not_retried()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), MockStep.Hang]);
        using var client = new PoliPageClient(Options(server, maxRetries: 2, timeout: Short));

        var act = () => client.Render.PdfAsync(Input);

        ShouldBeDownloadTimeout((await act.Should().ThrowAsync<PoliPageException>()).Which);
        server.Requests.Should().HaveCount(2, "the render, then one download");
    }

    [Fact]
    public async Task Pdf_timeout_bounds_the_whole_download_body_included()
    {
        // Every gap is far below the timeout, but the whole body is not.
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer().Drip()]);
        using var client = new PoliPageClient(Options(server, timeout: Short));

        var act = () => client.Render.PdfAsync(Input);

        ShouldBeDownloadTimeout((await act.Should().ThrowAsync<PoliPageException>()).Which);
    }

    [Fact]
    public async Task DownloadPdfAsync_is_bounded_by_the_client_timeout()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), MockStep.Hang]);
        using var client = new PoliPageClient(Options(server, timeout: Short));
        // A long timeout for the render, so only the download is under test.
        var document = await client.Render.DocumentAsync(Input, new RequestOptions { RequestTimeout = TimeSpan.FromSeconds(60) });

        var act = () => document.DownloadPdfAsync();

        ShouldBeDownloadTimeout((await act.Should().ThrowAsync<PoliPageException>()).Which);
    }

    [Fact]
    public async Task The_call_timeout_bounds_its_download()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), MockStep.Hang]);
        using var client = new PoliPageClient(Options(server, timeout: TimeSpan.FromSeconds(60)));

        var act = () => client.Render.PdfAsync(Input, new RequestOptions { RequestTimeout = Short });

        ShouldBeDownloadTimeout((await act.Should().ThrowAsync<PoliPageException>()).Which);
    }

    [Fact]
    public async Task PdfStream_whose_storage_never_answers_times_out_before_returning()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), MockStep.Hang]);
        using var client = new PoliPageClient(Options(server, timeout: Short));

        var act = () => client.Render.PdfStreamAsync(Input);

        ShouldBeDownloadTimeout((await act.Should().ThrowAsync<PoliPageException>()).Which);
    }

    [Fact]
    public async Task PdfStream_timeout_is_idle_so_a_slow_steady_PDF_is_read_whole()
    {
        // About 2 s in all, one byte every 50 ms: the timeout bounds each gap, not the total.
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer().Drip()]);
        using var client = new PoliPageClient(Options(server, timeout: Short));

        var stream = await client.Render.PdfStreamAsync(Input);
        await using (stream)
        {
            using var copy = new MemoryStream();
            await stream.CopyToAsync(copy);

            copy.ToArray().Should().Equal(Pdf);
        }
    }

    [Fact]
    public async Task PdfStream_that_stalls_mid_body_fails_its_read_with_the_timeout()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer().Stall()]);
        using var client = new PoliPageClient(Options(server, timeout: Short));

        var stream = await client.Render.PdfStreamAsync(Input);
        await using (stream)
        {
            var act = () => stream.CopyToAsync(Stream.Null);

            ShouldBeDownloadTimeout((await act.Should().ThrowAsync<PoliPageException>()).Which);

            // The stream stays failed with the same error.
            var again = async () => await stream.ReadAsync(new byte[16]);
            ShouldBeDownloadTimeout((await again.Should().ThrowAsync<PoliPageException>()).Which);
        }
    }

    [Fact]
    public async Task PdfStream_synchronous_reads_are_bounded_too()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer().Stall()]);
        using var client = new PoliPageClient(Options(server, timeout: Short));

        var stream = await client.Render.PdfStreamAsync(Input);
        await using (stream)
        {
            // Off the test thread, so a read that never ends fails the test instead of hanging it.
            var act = () => Task.Run(() => stream.CopyTo(Stream.Null)).WaitAsync(TimeSpan.FromSeconds(30));

            ShouldBeDownloadTimeout((await act.Should().ThrowAsync<PoliPageException>()).Which);
        }
    }

    [Fact]
    public async Task A_cancellation_during_the_download_is_aborted_not_a_timeout()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), MockStep.Hang]);
        using var client = new PoliPageClient(Options(server, timeout: TimeSpan.FromSeconds(60)));
        using var cts = new CancellationTokenSource();

        var call = client.Render.PdfAsync(Input, cancellationToken: cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));
        var act = () => call;

        var thrown = await act.Should().ThrowAsync<PoliPageException>();
        thrown.Which.Code.Should().Be(PoliPageErrorCode.Aborted);
    }

    [Fact]
    public async Task A_PDF_short_of_its_Content_Length_is_a_network_error_not_a_timeout()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer().Cut()]);
        using var client = new PoliPageClient(Options(server, timeout: TimeSpan.FromSeconds(60)));

        var act = () => client.Render.PdfAsync(Input);

        var thrown = await act.Should().ThrowAsync<PoliPageDownloadException>();
        thrown.Which.Message.Should().Be("Failed to download PDF: network error");
        thrown.Which.StatusCode.Should().Be(0);
        thrown.Which.IsRetryable().Should().BeFalse();
    }
}
