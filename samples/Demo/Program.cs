// PoliPage .NET SDK — runnable demo
//
// Run:  dotnet run --project samples/Demo
//
// Walks every public method of the SDK and writes the results to
// `samples/Demo/output/`. Renders `getting-started/welcome`: every
// organization gets a `getting-started` project, published into its `sandbox`
// environment when the organization is created, so a sandbox key
// (`pp_sandbox_…`) of a fresh organization renders it with no setup. In an
// organization that renamed, deleted or unpublished that project, the API
// answers 404 PROJECT_NOT_FOUND or PROJECT_NOT_IN_ENVIRONMENT.
//
// Reads POLI_PAGE_API_KEY (and the optional POLI_PAGE_BASE_URL) from the
// shell, then from `<repo>/.env`; with neither, it prompts for a sandbox key.
//
// Steps:
//   1. Render.PdfAsync            6. Documents.ThumbnailsAsync (Starter+ tier)
//   2. Render.PdfStreamAsync      7. Documents.PreviewAsync
//   3. RenderToFileAsync          8. Documents.DeleteAsync
//   4. Render.DocumentAsync       9. not-found (404 TEMPLATE_NOT_FOUND, caught)
//   5. Documents.GetAsync
//
// Open the generated files to confirm everything works:
//
//   - samples/Demo/output/render.pdf              (Render.PdfAsync)
//   - samples/Demo/output/stream.pdf              (Render.PdfStreamAsync)
//   - samples/Demo/output/file.pdf                (RenderToFileAsync)
//   - samples/Demo/output/documents_preview.html  (Documents.PreviewAsync, after storing)
//   - samples/Demo/output/thumbs/page_<n>.png     (Documents.ThumbnailsAsync, Starter+ tier)
//
// Step 9 (not-found) renders a template that does not exist on purpose: the
// API answers 404 TEMPLATE_NOT_FOUND, and the demo catches the
// PoliPageException and prints its code, status, message and request id. The
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

// The render input names a project and a template; there is no version to pick.
// The API renders the project's copy published in the API key's environment.
var welcomeInput = new RenderInput
{
    Project = "getting-started",
    Template = "welcome",
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

const int totalSteps = 9;

// ─────────────────────────────────────────────────────────────────────────────
// 1. Render.PdfAsync() — fetch PDF bytes into memory
//    Use when: small documents, you need the bytes synchronously (return from
//    an HTTP handler, attach to an email, hash for a signature).
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(1, totalSteps, "Render.PdfAsync() — PDF bytes in memory");
var pdf = await client.Render.PdfAsync(welcomeInput);
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
await using (var pdfStream = await client.Render.PdfStreamAsync(welcomeInput))
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
await client.RenderToFileAsync(welcomeInput, filePath);
Console.WriteLine($"  wrote {filePath}");
Console.WriteLine($"  {Ansi.Dim("open:")} {Ansi.FileLink(filePath)}");

// ─────────────────────────────────────────────────────────────────────────────
// 4. Render.DocumentAsync() — render and store the document, return its descriptor
//    Use when: you want the document persisted server-side for later access
//    (preview, thumbnails, re-download) without auto-fetching the PDF bytes.
//    Returns a DocumentDescriptor — persist `DocumentId` in your DB.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(4, totalSteps, "Render.DocumentAsync() — store the document, return the descriptor");
var doc = await client.Render.DocumentAsync(welcomeInput);
Console.WriteLine($"  {Ansi.Dim("documentId:")} {Ansi.Bold(doc.DocumentId)}");
Console.WriteLine($"  {Ansi.Dim("pageCount:")} {doc.PageCount}  {Ansi.Dim("sizeBytes:")} {doc.SizeBytes}");

// ─────────────────────────────────────────────────────────────────────────────
// 5. Documents.GetAsync(id) — refresh the descriptor (fresh presigned URL)
//    Use when: the original presigned URL has expired (~15 min TTL) and you
//    need a new one. Returns the same DocumentDescriptor shape.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(5, totalSteps, "Documents.GetAsync(id) — refresh descriptor");
var fetched = await client.Documents.GetAsync(doc.DocumentId);
Console.WriteLine($"  {Ansi.Dim("refreshed presigned URL valid until:")} {fetched.ExpiresAt:O}");

// ─────────────────────────────────────────────────────────────────────────────
// 6. Documents.ThumbnailsAsync(id, options) — per-page PNG images
//    Use when: rendering a thumbnail strip, a document picker, OG images.
//    Tier-gated on the API side: Free keys are rejected before any thumbnail
//    work happens. The API returns 403 FORBIDDEN / THUMBNAILS_NOT_AVAILABLE
//    depending on the gating layer that catches the call. We treat any of those as "soft skip" so the demo keeps running on
//    a Free key without lying about the surface.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(6, totalSteps, "Documents.ThumbnailsAsync(id) — page images (Starter+ tier)");
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
        await File.WriteAllBytesAsync(thumbPath, Convert.FromBase64String(thumb.Data));
        Console.WriteLine($"  wrote page_{thumb.Page}.png ({thumb.Width}x{thumb.Height})");
    }
    Console.WriteLine($"  {Ansi.Dim("open:")} {Ansi.FileLink(thumbDir)}");
}
catch (PoliPageException ex) when (
    ex.Code == PoliPageErrorCode.ThumbnailsNotAvailable
    || (ex is PoliPageAuthException && ex.StatusCode == 403))
{
    Console.WriteLine(
        $"  {Ansi.Yellow("skipped")} — {ex.Code} (HTTP {ex.StatusCode}) — Starter+ tier feature");
}

// ─────────────────────────────────────────────────────────────────────────────
// 7. Documents.PreviewAsync(id) — get the stored document's HTML preview
//    Use when: rendering a live editor over a stored document, building a
//    review UI, snapshot tests in CI. No counter increments — the engine
//    performs no work on this call. Returns { Html, PageCount }.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(7, totalSteps, "Documents.PreviewAsync(id) — stored document HTML (no engine work)");
var preview = await client.Documents.PreviewAsync(doc.DocumentId);
var previewPath = Path.Combine(outDir, "documents_preview.html");
await File.WriteAllTextAsync(previewPath, preview.Html);
Console.WriteLine($"  {Ansi.Bold(preview.PageCount.ToString())} page(s), {preview.Html.Length} chars");
Console.WriteLine($"  {Ansi.Dim("open:")} {Ansi.FileLink(previewPath)}");

// ─────────────────────────────────────────────────────────────────────────────
// 8. Documents.DeleteAsync(id) — soft-delete the stored document
//    Use when: cleaning up demo / test runs, GDPR-style erasure. The document
//    is hidden from subsequent reads; a re-delete also succeeds (idempotent).
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(8, totalSteps, "Documents.DeleteAsync(id) — soft-delete");
await client.Documents.DeleteAsync(doc.DocumentId);
Console.WriteLine($"  {Ansi.Green("✔")} deleted {doc.DocumentId}");

// ─────────────────────────────────────────────────────────────────────────────
// 9. not-found — DELIBERATELY render a template that does not exist, then
//    catch the error. API errors, network failures and timeouts surface as
//    `PoliPageException` (or a more specific subclass: PoliPageNotFoundException,
//    PoliPageAuthException, etc.). Inspect `Code`, `StatusCode`, `Message`,
//    `RequestId`, or pattern-match on the subclass. Cancelling your own
//    `CancellationToken` is not an SDK error: it propagates as
//    `OperationCanceledException`, never as `PoliPageException`.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(9, totalSteps, "not-found — DEMO ONLY (we trigger an error on purpose)");
Console.WriteLine(Ansi.Yellow("  ⚠  This step is intentional — the SDK is about to throw, but the"));
Console.WriteLine(Ansi.Yellow("     demo will catch and inspect it. ") + Ansi.Bold("The demo is NOT crashing."));
Console.WriteLine(Ansi.Dim("     (We render a template that does not exist, expecting the API to return 404 TEMPLATE_NOT_FOUND.)"));
Console.WriteLine();
try
{
    // Intentionally invalid: template 'does-not-exist' gets 404 TEMPLATE_NOT_FOUND.
    // `new { }` goes on the wire as `"data": {}`.
    await client.Render.PdfAsync(new RenderInput
    {
        Project = "getting-started",
        Template = "does-not-exist",
        Data = new { },
    });
    Console.WriteLine("  " + Ansi.Red("✗ unexpected: the call succeeded but should have failed"));
}
catch (PoliPageException ex)
{
    Console.WriteLine($"  {Ansi.Green("✔")} Error caught successfully. PoliPageException exposed:");
    Console.WriteLine($"      Code:           {ex.Code}");
    Console.WriteLine($"      StatusCode:     {ex.StatusCode}");
    Console.WriteLine($"      Message:        {ex.Message}");
    Console.WriteLine($"      RequestId:      {ex.RequestId ?? "(none)"}");
    Console.WriteLine($"      Type:           {ex.GetType().Name}");
    Console.WriteLine($"      IsNotFound:     {ex is PoliPageNotFoundException}");
}

Console.WriteLine();
Console.WriteLine($"{Ansi.Green("✔")} {Ansi.Bold("All steps completed.")} Inspect output in {Ansi.FileLink(outDir)}");
Console.WriteLine();
