// Demonstrates: client.Render.PdfStreamAsync(input) — streams the PDF without
// buffering it in memory. Dispose the stream.
using PoliPage;

var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
});

await using Stream pdf = await client.Render.PdfStreamAsync(new RenderInput
{
    Project  = "billing",
    Template = "invoice",
    Data     = new Dictionary<string, object>
    {
        { "invoiceNumber", "INV-001" },
        { "total", 1280 },
    },
});

await using var file = File.Create("INV-001.pdf");
await pdf.CopyToAsync(file);
