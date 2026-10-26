// Demonstrates: client.Render.PdfAsync(input) — renders the project's copy
// published in the API key's environment.
using PoliPage;

var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
});

byte[] pdf = await client.Render.PdfAsync(new RenderInput
{
    Project  = "billing",
    Template = "invoice",
    Data     = new Dictionary<string, object>
    {
        { "invoiceNumber", "INV-001" },
        { "total", 1280 },
    },
});

// `pdf` is a byte[] of PDF bytes.
Console.WriteLine($"Rendered {pdf.Length} bytes");
