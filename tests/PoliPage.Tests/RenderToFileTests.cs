using FluentAssertions;
using PoliPage.Tests.Contract;
using Xunit;
using static PoliPage.Tests.TransportTestSupport;

namespace PoliPage.Tests;

/// <summary>
/// <c>RenderToFileAsync</c> writes the PDF to a temporary file next to the target and moves
/// it into place only once it is complete; a file it cannot write is <c>io_error</c>, and any
/// failure leaves the target as it was.
/// </summary>
public sealed class RenderToFileTests : IDisposable
{
    private static readonly byte[] Previous = [0x01, 0x02, 0x03];

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"polipage-render-to-file-{Guid.NewGuid():N}");

    public RenderToFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private static MockStep PdfAnswer() => Bytes(200, Pdf, "application/pdf");

    private string[] Entries(string? directory = null)
        => [.. Directory.EnumerateFileSystemEntries(directory ?? _directory).Select(Path.GetFileName).Order(StringComparer.Ordinal)!];

    private static void ShouldBeIoError(PoliPageException error)
    {
        error.Code.Should().Be(PoliPageErrorCode.IoError);
        error.InnerException.Should().NotBeNull();
        error.Message.Should().Be("Failed to write PDF: " + error.InnerException!.Message);
        error.StatusCode.Should().Be(0);
        error.RequestId.Should().BeNull();
        error.IsRetryable().Should().BeFalse();
        error.ToPayload().Status.Should().BeNull();
    }

    [Fact]
    public async Task Writes_the_PDF_and_leaves_no_temporary_file()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer()]);
        using var client = new PoliPageClient(Options(server));
        var path = Path.Combine(_directory, "invoice.pdf");

        await client.RenderToFileAsync(Input, path);

        (await File.ReadAllBytesAsync(path)).Should().Equal(Pdf);
        Entries().Should().Equal("invoice.pdf");
    }

    [Fact]
    public async Task Creates_the_missing_parent_directories()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer()]);
        using var client = new PoliPageClient(Options(server));
        var path = Path.Combine(_directory, "2026", "11", "invoice.pdf");

        await client.RenderToFileAsync(Input, path);

        (await File.ReadAllBytesAsync(path)).Should().Equal(Pdf);
    }

    [Fact]
    public async Task Replaces_an_existing_file()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer()]);
        using var client = new PoliPageClient(Options(server));
        var path = Path.Combine(_directory, "invoice.pdf");
        await File.WriteAllBytesAsync(path, Previous);

        await client.RenderToFileAsync(Input, path);

        (await File.ReadAllBytesAsync(path)).Should().Equal(Pdf);
        Entries().Should().Equal("invoice.pdf");
    }

    [Fact]
    public async Task A_directory_that_cannot_be_created_is_io_error_before_any_render()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer()]);
        using var client = new PoliPageClient(Options(server));
        // A regular file where a parent directory should be.
        var blocker = Path.Combine(_directory, "not-a-directory");
        await File.WriteAllBytesAsync(blocker, Previous);

        var act = () => client.RenderToFileAsync(Input, Path.Combine(blocker, "invoice.pdf"));

        var thrown = await act.Should().ThrowAsync<PoliPageException>();
        ShouldBeIoError(thrown.Which);
        thrown.Which.InnerException.Should().BeAssignableTo<IOException>();
        server.Requests.Should().BeEmpty("a path that cannot be written costs no render");
        (await File.ReadAllBytesAsync(blocker)).Should().Equal(Previous);
    }

    [Fact]
    public async Task A_target_that_cannot_be_replaced_is_io_error_and_is_left_untouched()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer()]);
        using var client = new PoliPageClient(Options(server));
        // A non-empty directory at the target path: no file can be moved over it.
        var target = Path.Combine(_directory, "invoice.pdf");
        Directory.CreateDirectory(target);
        await File.WriteAllBytesAsync(Path.Combine(target, "keep"), Previous);

        var act = () => client.RenderToFileAsync(Input, target);

        ShouldBeIoError((await act.Should().ThrowAsync<PoliPageException>()).Which);
        Directory.Exists(target).Should().BeTrue();
        Entries(target).Should().Equal("keep");
        Entries().Should().Equal("invoice.pdf");
    }

    [Fact]
    public async Task A_failed_render_leaves_an_existing_file_as_it_was()
    {
        var notFound = JsonAnswer(404, """{"error":"TEMPLATE_NOT_FOUND","detail":"no such template"}""");
        await using var server = new MockServer(_ => [notFound]);
        using var client = new PoliPageClient(Options(server));
        var path = Path.Combine(_directory, "invoice.pdf");
        await File.WriteAllBytesAsync(path, Previous);

        var act = () => client.RenderToFileAsync(Input, path);

        (await act.Should().ThrowAsync<PoliPageNotFoundException>()).Which.Code.Should().Be(PoliPageErrorCode.TemplateNotFound);
        (await File.ReadAllBytesAsync(path)).Should().Equal(Previous);
        Entries().Should().Equal("invoice.pdf");
    }

    [Fact]
    public async Task A_PDF_short_of_its_Content_Length_is_never_written()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer().Cut()]);
        using var client = new PoliPageClient(Options(server));
        var existing = Path.Combine(_directory, "existing.pdf");
        await File.WriteAllBytesAsync(existing, Previous);

        var act = () => client.RenderToFileAsync(Input, existing);

        var thrown = await act.Should().ThrowAsync<PoliPageDownloadException>();
        thrown.Which.Message.Should().Be("Failed to download PDF: network error");
        (await File.ReadAllBytesAsync(existing)).Should().Equal(Previous);
        Entries().Should().Equal("existing.pdf");
    }

    [Fact]
    public async Task A_PDF_short_of_its_Content_Length_creates_no_file()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer().Cut()]);
        using var client = new PoliPageClient(Options(server));

        var act = () => client.RenderToFileAsync(Input, Path.Combine(_directory, "missing.pdf"));

        await act.Should().ThrowAsync<PoliPageDownloadException>();
        Entries().Should().BeEmpty();
    }

    [Fact]
    public async Task A_cancelled_write_is_aborted_and_creates_no_file()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer().Drip()]);
        using var client = new PoliPageClient(Options(server));
        using var cts = new CancellationTokenSource();

        var call = client.RenderToFileAsync(Input, Path.Combine(_directory, "invoice.pdf"), cancellationToken: cts.Token);
        cts.CancelAfter(TimeSpan.FromMilliseconds(300));
        var act = () => call;

        (await act.Should().ThrowAsync<PoliPageException>()).Which.Code.Should().Be(PoliPageErrorCode.Aborted);
        Entries().Should().BeEmpty();
    }
}
