// PoliPage .NET SDK — runnable demo
//
// Run:  dotnet run --project samples/Demo
//
// Walks every public method of the SDK and writes the results to
// `samples/Demo/output/`. Uses the `getting-started/welcome/1.0.0` project
// template that's auto-provisioned in every Poli Page org, so this works out
// of the box with any fresh API key — no project setup needed.
//
// Open the generated files to confirm everything works:
//
//   - samples/Demo/output/render.pdf              (Render.PdfAsync)
//   - samples/Demo/output/stream.pdf              (Render.PdfStreamAsync)
//   - samples/Demo/output/file.pdf                (RenderToFileAsync)
//   - samples/Demo/output/render_preview.html     (Render.PreviewAsync)
//   - samples/Demo/output/documents_preview.html  (Documents.PreviewAsync, after storing)
//   - samples/Demo/output/thumbs/page_<n>.png     (Documents.ThumbnailsAsync, Starter+ tier)
//
// Step 10 deliberately triggers a 400 to exercise the error-handling story —
// the demo catches PoliPageException and prints the exposed fields. The
// script does NOT crash there.

using System.Text;
using PoliPage;
using PoliPage.Demo;

// ─────────────────────────────────────────────────────────────────────────────
// Setup
// ─────────────────────────────────────────────────────────────────────────────

var repoRoot = RepoPaths.FindRepoRoot();
var outDir = Path.Combine(repoRoot, "samples", "Demo", "output");
Directory.CreateDirectory(outDir);

// Resolve the API key — process env wins, then `<repo>/.env`, then prompt.
// On a fresh prompt the pasted key is saved to `<repo>/.env` automatically.
var apiKey = EnvFile.EnsureApiKey(repoRoot);
var baseUrl = EnvFile.ResolveBaseUrl(repoRoot);

// Every render call uses project mode — required by PdfAsync / PdfStreamAsync /
// RenderToFileAsync / DocumentAsync. `getting-started/welcome` is auto-provisioned
// in every org, so this works out of the box for any newcomer with a fresh API key.
var projectInput = new ProjectModeInput
{
    Project = "getting-started",
    Template = "welcome",
    Version = "1.0.0",
    Data = new { name = "SDK Demo" },
};

// The client is a single object you create once and reuse for every call.
// Hooks are optional — they let you observe retry/error activity without
// coupling to a logging library.
using var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = apiKey,
    BaseUrl = baseUrl,
    OnRetry = e => Console.WriteLine(
        Ansi.Yellow("  ↻") + " "
        + Ansi.Dim($"retrying attempt {e.Attempt} after {e.Delay.TotalMilliseconds:F0}ms: {e.Reason}")),
    OnError = ex => Console.WriteLine(
        Ansi.Red("  ✗") + " "
        + Ansi.Dim($"{ex.GetType().Name}: {ex.Message}")),
});

const int totalSteps = 10;

// ─────────────────────────────────────────────────────────────────────────────
// 1. Render.PdfAsync() — fetch PDF bytes into memory
//    Use when: small documents, you need the bytes synchronously (return from
//    an HTTP handler, attach to an email, hash for a signature).
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(1, totalSteps, "Render.PdfAsync() — PDF bytes in memory");
var pdf = await client.Render.PdfAsync(projectInput);
var renderPath = Path.Combine(outDir, "render.pdf");
await File.WriteAllBytesAsync(renderPath, pdf);
var magic = Encoding.ASCII.GetString(pdf, 0, Math.Min(4, pdf.Length));
Console.WriteLine($"  {pdf.Length} bytes, magic: {Ansi.Bold(magic)}");
Console.WriteLine($"  {Ansi.Dim("open:")} {Ansi.FileLink(renderPath)}");

// ─────────────────────────────────────────────────────────────────────────────
// 2. Render.PdfStreamAsync() — get a Stream of PDF bytes
//    Use when: large documents, piping to S3 / an HTTP response / a transformer.
//    Memory-bounded — never holds the whole PDF in RAM.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(2, totalSteps, "Render.PdfStreamAsync() — Stream of PDF bytes");
var streamPath = Path.Combine(outDir, "stream.pdf");
long streamBytes;
await using (var pdfStream = await client.Render.PdfStreamAsync(projectInput))
await using (var fileStream = File.Create(streamPath))
{
    await pdfStream.CopyToAsync(fileStream);
    streamBytes = fileStream.Length;
}
Console.WriteLine($"  {streamBytes} bytes streamed");
Console.WriteLine($"  {Ansi.Dim("open:")} {Ansi.FileLink(streamPath)}");

// ─────────────────────────────────────────────────────────────────────────────
// 3. RenderToFileAsync() — render straight to disk
//    Use when: you just want a PDF on the filesystem. Built on PdfStreamAsync,
//    so memory usage stays bounded regardless of document size.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(3, totalSteps, "RenderToFileAsync() — render straight to disk");
var filePath = Path.Combine(outDir, "file.pdf");
await client.RenderToFileAsync(projectInput, filePath);
Console.WriteLine($"  wrote {filePath}");
Console.WriteLine($"  {Ansi.Dim("open:")} {Ansi.FileLink(filePath)}");

// ─────────────────────────────────────────────────────────────────────────────
// 4. Render.PreviewAsync() — paginated HTML for an editor / review UI
//    Use when: rendering a live editor, snapshot tests in CI, side-by-side
//    diff of template changes. No file is written by the engine — you get the
//    HTML in-memory. Useful before committing to a PDF render.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(4, totalSteps, "Render.PreviewAsync() — paginated HTML");
var renderPreview = await client.Render.PreviewAsync(projectInput);
var renderPreviewPath = Path.Combine(outDir, "render_preview.html");
await File.WriteAllTextAsync(renderPreviewPath, renderPreview.Html);
Console.WriteLine(
    $"  {Ansi.Bold(renderPreview.TotalPages.ToString())} page(s), {renderPreview.Html.Length} chars,"
    + $" env={renderPreview.Environment}");
Console.WriteLine($"  {Ansi.Dim("open:")} {Ansi.FileLink(renderPreviewPath)}");

// ─────────────────────────────────────────────────────────────────────────────
// 5. Render.DocumentAsync() — render and store the document, return its descriptor
//    Use when: you want the document persisted server-side for later access
//    (preview, thumbnails, re-download) without auto-fetching the PDF bytes.
//    Returns a DocumentDescriptor — persist `DocumentId` in your DB.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(5, totalSteps, "Render.DocumentAsync() — store the document, return the descriptor");
var doc = await client.Render.DocumentAsync(projectInput);
Console.WriteLine($"  {Ansi.Dim("documentId:")} {Ansi.Bold(doc.DocumentId)}");
Console.WriteLine($"  {Ansi.Dim("pageCount:")} {doc.PageCount}  {Ansi.Dim("sizeBytes:")} {doc.SizeBytes}");

// ─────────────────────────────────────────────────────────────────────────────
// 6. Documents.GetAsync(id) — refresh the descriptor (fresh presigned URL)
//    Use when: the original presigned URL has expired (~15 min TTL) and you
//    need a new one. Returns the same DocumentDescriptor shape.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(6, totalSteps, "Documents.GetAsync(id) — refresh descriptor");
var fetched = await client.Documents.GetAsync(doc.DocumentId);
Console.WriteLine($"  {Ansi.Dim("refreshed presigned URL valid until:")} {fetched.ExpiresAt:O}");

// ─────────────────────────────────────────────────────────────────────────────
// 7. Documents.ThumbnailsAsync(id, options) — per-page PNG images
//    Use when: rendering a thumbnail strip, a document picker, OG images.
//    Tier-gated on the API side: Free keys are rejected before any thumbnail
//    work happens. The API returns 402 PAYMENT_REQUIRED or 403 FORBIDDEN /
//    THUMBNAILS_NOT_AVAILABLE depending on the gating layer that catches the
//    call. We treat any of those as "soft skip" so the demo keeps running on
//    a Free key without lying about the surface.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(7, totalSteps, "Documents.ThumbnailsAsync(id) — page images (Starter+ tier)");
try
{
    var thumbs = await client.Documents.ThumbnailsAsync(
        doc.DocumentId,
        new ThumbnailOptions { Width = 320, Format = ThumbnailFormat.Png });
    var thumbDir = Path.Combine(outDir, "thumbs");
    Directory.CreateDirectory(thumbDir);
    foreach (var thumb in thumbs)
    {
        var thumbPath = Path.Combine(thumbDir, $"page_{thumb.Page}.png");
        await File.WriteAllBytesAsync(thumbPath, Convert.FromBase64String(thumb.Base64Data));
        Console.WriteLine($"  wrote page_{thumb.Page}.png ({thumb.Width}x{thumb.Height})");
    }
    Console.WriteLine($"  {Ansi.Dim("open:")} {Ansi.FileLink(thumbDir)}");
}
catch (PoliPageException ex) when (
    ex.Code == "THUMBNAILS_NOT_AVAILABLE"
    || ex is PoliPagePaymentRequiredException
    || (ex is PoliPageAuthException && ex.StatusCode == 403))
{
    Console.WriteLine(
        $"  {Ansi.Yellow("skipped")} — {ex.Code} (HTTP {ex.StatusCode}) — Starter+ tier feature");
}

// ─────────────────────────────────────────────────────────────────────────────
// 8. Documents.PreviewAsync(id) — get the stored document's HTML preview
//    Use when: rendering a live editor over a stored document, building a
//    review UI, snapshot tests in CI. No counter increments — the engine
//    performs no work on this call. Returns { Html, PageCount }.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(8, totalSteps, "Documents.PreviewAsync(id) — stored document HTML (no engine work)");
var preview = await client.Documents.PreviewAsync(doc.DocumentId);
var previewPath = Path.Combine(outDir, "documents_preview.html");
await File.WriteAllTextAsync(previewPath, preview.Html);
Console.WriteLine($"  {Ansi.Bold(preview.PageCount.ToString())} page(s), {preview.Html.Length} chars");
Console.WriteLine($"  {Ansi.Dim("open:")} {Ansi.FileLink(previewPath)}");

// ─────────────────────────────────────────────────────────────────────────────
// 9. Documents.DeleteAsync(id) — soft-delete the stored document
//    Use when: cleaning up demo / test runs, GDPR-style erasure. The document
//    is hidden from subsequent reads; re-delete returns Gone.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(9, totalSteps, "Documents.DeleteAsync(id) — soft-delete");
await client.Documents.DeleteAsync(doc.DocumentId);
Console.WriteLine($"  {Ansi.Green("✔")} deleted {doc.DocumentId}");

// ─────────────────────────────────────────────────────────────────────────────
// 10. Error handling — DELIBERATELY trigger a failure, then catch it.
//    Every failure — API errors, network failures, timeouts, caller aborts —
//    surfaces as `PoliPageException` (or a more specific subclass:
//    PoliPageAuthException, PoliPageRateLimitException, etc.). Inspect `Code`,
//    `StatusCode`, `RequestId`, or pattern-match on the subclass.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(10, totalSteps, "error handling — DEMO ONLY (we trigger an error on purpose)");
Console.WriteLine(Ansi.Yellow("  ⚠  This step is intentional — the SDK is about to throw, but the"));
Console.WriteLine(Ansi.Yellow("     demo will catch and inspect it. ") + Ansi.Bold("The demo is NOT crashing."));
Console.WriteLine(Ansi.Dim("     (We send an invalid version string, expecting the API to return 400 INVALID_VERSION_FORMAT.)"));
Console.WriteLine();
try
{
    // Intentionally invalid: version 'banana' triggers INVALID_VERSION_FORMAT (400).
    await client.Render.PdfAsync(new ProjectModeInput
    {
        Project = "getting-started",
        Template = "welcome",
        Version = "banana",
        Data = new { },
    });
    Console.WriteLine("  " + Ansi.Red("✗ unexpected: the call succeeded but should have failed"));
}
catch (PoliPageException ex)
{
    Console.WriteLine($"  {Ansi.Green("✔")} Error caught successfully. PoliPageException exposed:");
    Console.WriteLine($"      Code:           {ex.Code}");
    Console.WriteLine($"      StatusCode:     {ex.StatusCode}");
    Console.WriteLine($"      RequestId:      {ex.RequestId ?? "(none)"}");
    Console.WriteLine($"      Type:           {ex.GetType().Name}");
    Console.WriteLine($"      IsAuthError:    {ex is PoliPageAuthException}");
    Console.WriteLine($"      IsRateLimit:    {ex is PoliPageRateLimitException}");
    Console.WriteLine($"      IsValidation:   {ex is PoliPageValidationException}");
}

Console.WriteLine();
Console.WriteLine($"{Ansi.Green("✔")} {Ansi.Bold("All steps completed.")} Inspect output in {Ansi.FileLink(outDir)}");
Console.WriteLine();
