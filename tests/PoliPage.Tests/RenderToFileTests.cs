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
        if (!Directory.Exists(_directory))
            return;

        // A read-only file blocks the delete on Windows.
        foreach (var file in Directory.EnumerateFiles(_directory, "*", SearchOption.AllDirectories))
        {
            if (new FileInfo(file).LinkTarget is null)
                File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_directory, recursive: true);
    }

    private static MockStep PdfAnswer() => Bytes(200, Pdf, "application/pdf");

    /// <summary>The path with its links resolved (the temporary directory may sit behind one, as on macOS).</summary>
    private static string ResolvedPath(string path)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(path)!);
        var parts = new Stack<string>();
        for (var d = directory; d is not null; d = d.Parent)
        {
            if (d.LinkTarget is not null)
                return Path.Combine([d.ResolveLinkTarget(returnFinalTarget: true)!.FullName, .. parts, Path.GetFileName(path)]);
            parts.Push(d.Name);
        }

        return path;
    }

    private static void MakeFifo(string path)
    {
        using var mkfifo = System.Diagnostics.Process.Start("mkfifo", [path])!;
        mkfifo.WaitForExit();
        mkfifo.ExitCode.Should().Be(0, "mkfifo creates the FIFO");
    }

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

        var thrown = await act.Should().ThrowAsync<PoliPageException>();
        ShouldBeIoError(thrown.Which);
        thrown.Which.Message.Should().Be($"Failed to write PDF: {thrown.Which.InnerException!.Message}")
            .And.EndWith(": not a regular file");
        server.Requests.Should().BeEmpty("a target that cannot be written costs no render");
        Directory.Exists(target).Should().BeTrue();
        Entries(target).Should().Equal("keep");
        Entries().Should().Equal("invoice.pdf");
    }

    [UnixFact]
    public async Task A_symlinked_target_replaces_the_file_it_points_at_and_stays_a_link()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer()]);
        using var client = new PoliPageClient(Options(server));
        var real = Path.Combine(_directory, "real.pdf");
        var link = Path.Combine(_directory, "link.pdf");
        await File.WriteAllBytesAsync(real, Previous);
        File.CreateSymbolicLink(link, "real.pdf");

        await client.RenderToFileAsync(Input, link);

        new FileInfo(link).LinkTarget.Should().Be("real.pdf");
        (await File.ReadAllBytesAsync(real)).Should().Equal(Pdf);
        Entries().Should().Equal("link.pdf", "real.pdf");
    }

    [UnixFact]
    public async Task A_link_to_a_missing_file_creates_that_file()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer()]);
        using var client = new PoliPageClient(Options(server));
        var target = Path.Combine(_directory, "out", "invoice.pdf");
        var link = Path.Combine(_directory, "link.pdf");
        File.CreateSymbolicLink(link, target);

        await client.RenderToFileAsync(Input, link);

        new FileInfo(link).LinkTarget.Should().Be(target);
        (await File.ReadAllBytesAsync(target)).Should().Equal(Pdf);
    }

    [UnixFact]
    public async Task A_relative_link_resolves_against_its_real_directory()
    {
        // sym -> real/dir, and real/dir/link -> ../a.pdf: through sym, the link's real
        // directory is real/dir, so the file is real/a.pdf, not the a.pdf next to sym.
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer()]);
        using var client = new PoliPageClient(Options(server));
        var realDir = Path.Combine(_directory, "real", "dir");
        Directory.CreateDirectory(realDir);
        Directory.CreateSymbolicLink(Path.Combine(_directory, "sym"), Path.Combine("real", "dir"));
        File.CreateSymbolicLink(Path.Combine(realDir, "link"), Path.Combine("..", "a.pdf"));
        var file = Path.Combine(_directory, "real", "a.pdf");
        await File.WriteAllBytesAsync(file, Previous);

        await client.RenderToFileAsync(Input, Path.Combine(_directory, "sym", "link"));

        (await File.ReadAllBytesAsync(file)).Should().Equal(Pdf);
        File.Exists(Path.Combine(_directory, "a.pdf")).Should().BeFalse();
        new FileInfo(Path.Combine(realDir, "link")).LinkTarget.Should().Be(Path.Combine("..", "a.pdf"));
        Entries(Path.Combine(_directory, "real")).Should().Equal("a.pdf", "dir");
    }

    [UnixFact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task An_existing_file_keeps_its_mode()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer()]);
        using var client = new PoliPageClient(Options(server));
        var path = Path.Combine(_directory, "invoice.pdf");
        await File.WriteAllBytesAsync(path, Previous);
        const UnixFileMode Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        File.SetUnixFileMode(path, Mode);

        await client.RenderToFileAsync(Input, path);

        (await File.ReadAllBytesAsync(path)).Should().Equal(Pdf);
        File.GetUnixFileMode(path).Should().Be(Mode);
    }

    [UnixFact]
    public async Task A_FIFO_target_is_io_error_at_once_and_is_never_opened()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer()]);
        using var client = new PoliPageClient(Options(server));
        var fifo = Path.Combine(_directory, "invoice.pdf");
        MakeFifo(fifo);

        // Opening a FIFO with no reader blocks: a hang fails the test instead of the run.
        var act = () => client.RenderToFileAsync(Input, fifo).WaitAsync(TimeSpan.FromSeconds(10));

        var thrown = await act.Should().ThrowAsync<PoliPageException>();
        ShouldBeIoError(thrown.Which);
        thrown.Which.Message.Should().Be($"Failed to write PDF: {ResolvedPath(fifo)}: not a regular file");
        server.Requests.Should().BeEmpty("a target that cannot be written costs no render");
        Entries().Should().Equal("invoice.pdf");
        File.GetAttributes(fifo).HasFlag(FileAttributes.Directory).Should().BeFalse();
    }

    [UnixFact]
    public async Task A_device_target_is_io_error_before_any_render()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer()]);
        using var client = new PoliPageClient(Options(server));

        var act = () => client.RenderToFileAsync(Input, "/dev/null");

        var thrown = await act.Should().ThrowAsync<PoliPageException>();
        ShouldBeIoError(thrown.Which);
        thrown.Which.Message.Should().Be("Failed to write PDF: /dev/null: not a regular file");
        server.Requests.Should().BeEmpty();
    }

    [UnixNonRootFact]
    [System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
    public async Task The_owner_write_bit_decides_for_the_owner_whatever_the_others_bits()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer()]);
        using var client = new PoliPageClient(Options(server));
        var path = Path.Combine(_directory, "invoice.pdf");
        await File.WriteAllBytesAsync(path, Previous);
        // r---w--w-: others may write, the owner (this process) may not.
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite);

        var act = () => client.RenderToFileAsync(Input, path);

        ShouldBeIoError((await act.Should().ThrowAsync<PoliPageException>()).Which);
        server.Requests.Should().BeEmpty();
        File.SetUnixFileMode(path, UnixFileMode.UserRead);
        (await File.ReadAllBytesAsync(path)).Should().Equal(Previous);
    }

    [WritableTargetCheckFact]
    public async Task A_read_only_target_is_io_error_before_any_render_and_is_not_replaced()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), PdfAnswer()]);
        using var client = new PoliPageClient(Options(server));
        var path = Path.Combine(_directory, "invoice.pdf");
        await File.WriteAllBytesAsync(path, Previous);
        if (OperatingSystem.IsWindows())
            File.SetAttributes(path, FileAttributes.ReadOnly);
        else
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        var act = () => client.RenderToFileAsync(Input, path);

        var thrown = await act.Should().ThrowAsync<PoliPageException>();
        ShouldBeIoError(thrown.Which);
        thrown.Which.InnerException.Should().BeOfType<UnauthorizedAccessException>();
        server.Requests.Should().BeEmpty("a file that cannot be written costs no render");
        (await File.ReadAllBytesAsync(path)).Should().Equal(Previous);
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

/// <summary>Skipped on Windows, where symbolic links need a privilege and modes do not exist.</summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Unix only: symbolic links need a privilege on Windows, and Unix file modes do not apply there.";
    }
}

/// <summary>Skipped on Unix with an effective uid of 0, which a read-only mode does not stop.</summary>
public sealed class WritableTargetCheckFactAttribute : FactAttribute
{
    public WritableTargetCheckFactAttribute()
    {
        if (!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess)
            Skip = "root can write a read-only file.";
    }
}

/// <summary>Unix only (it sets Unix modes), and skipped with an effective uid of 0.</summary>
public sealed class UnixNonRootFactAttribute : FactAttribute
{
    public UnixNonRootFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Unix only: it sets Unix file modes.";
        else if (Environment.IsPrivilegedProcess)
            Skip = "root can write a read-only file.";
    }
}
