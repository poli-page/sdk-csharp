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
//   - samples/Demo/output/render.pdf   (from client.Render.PdfAsync())
//   - samples/Demo/output/stream.pdf   (from client.Render.PdfStreamAsync())
//   - samples/Demo/output/file.pdf     (from client.RenderToFileAsync())
//   - samples/Demo/output/preview.html (from client.Documents.PreviewAsync(id),
//                                       after storing the document via
//                                       client.Render.DocumentAsync())
//
// Note: thumbnails are available against stored documents via
// client.Documents.ThumbnailsAsync() — that requires Starter+ tier.

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

const int totalSteps = 6;

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
// 4. Render.DocumentAsync() — render and store the document, return its descriptor
//    Use when: you want the document persisted server-side for later access
//    (preview, thumbnails, re-download) without auto-fetching the PDF bytes.
//    Returns a DocumentDescriptor — persist `DocumentId` in your DB.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(4, totalSteps, "Render.DocumentAsync() — store the document, return the descriptor");
var doc = await client.Render.DocumentAsync(projectInput);
Console.WriteLine($"  {Ansi.Dim("documentId:")} {Ansi.Bold(doc.DocumentId)}");

// ─────────────────────────────────────────────────────────────────────────────
// 5. Documents.PreviewAsync(id) — get the stored document's HTML preview
//    Use when: rendering a live editor over a stored document, building a
//    review UI, snapshot tests in CI. No counter increments — the engine
//    performs no work on this call. Returns { Html, PageCount }.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(5, totalSteps, "Documents.PreviewAsync(id) — stored document HTML (no engine work)");
var preview = await client.Documents.PreviewAsync(doc.DocumentId);
var previewPath = Path.Combine(outDir, "preview.html");
await File.WriteAllTextAsync(previewPath, preview.Html);
Console.WriteLine($"  {Ansi.Bold(preview.PageCount.ToString())} page(s), {preview.Html.Length} chars");
Console.WriteLine($"  {Ansi.Dim("open:")} {Ansi.FileLink(previewPath)}");

// ─────────────────────────────────────────────────────────────────────────────
// 6. Error handling — DELIBERATELY trigger a failure, then catch it.
//    Every failure — API errors, network failures, timeouts, caller aborts —
//    surfaces as `PoliPageException` (or a more specific subclass:
//    PoliPageAuthException, PoliPageRateLimitException, etc.). Inspect `Code`,
//    `StatusCode`, `RequestId`, or pattern-match on the subclass.
// ─────────────────────────────────────────────────────────────────────────────
Ansi.Step(6, totalSteps, "error handling — DEMO ONLY (we trigger an error on purpose)");
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
