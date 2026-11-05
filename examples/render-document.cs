// Demonstrates: client.Render.DocumentAsync(input) — renders and stores the
// document, returns its descriptor without downloading the PDF.
using PoliPage;

var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
});

DocumentDescriptor doc = await client.Render.DocumentAsync(new RenderInput
{
    Project  = "billing",
    Template = "invoice",
    Data     = new Dictionary<string, object>
    {
        { "invoiceNumber", "INV-001" },
        { "total", 1280 },
    },
});

Console.WriteLine($"{doc.DocumentId}: {doc.PageCount} page(s), {doc.SizeBytes} bytes");
Console.WriteLine($"PDF (until {doc.ExpiresAt:u}): {doc.PresignedPdfUrl}");

// Fetch the bytes later through the SDK's download transport.
byte[] pdf = await doc.DownloadPdfAsync();
