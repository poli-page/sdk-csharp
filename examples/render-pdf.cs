// Demonstrates: client.Render.PdfAsync(input) — project mode only.
using PoliPage;

var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
});

byte[] pdf = await client.Render.PdfAsync(new ProjectModeInput
{
    Project  = "billing",
    Template = "invoice",
    Version  = "1.0.0",
    Data     = new Dictionary<string, object>
    {
        { "invoiceNumber", "INV-001" },
        { "total", 1280 },
    },
});

// `pdf` is a byte[] of PDF bytes.
Console.WriteLine($"Rendered {pdf.Length} bytes");
