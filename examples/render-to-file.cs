// Demonstrates: client.RenderToFileAsync(input, path) — renders and writes the
// PDF to a file. The target is replaced only once the whole PDF is written.
using PoliPage;

var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
});

await client.RenderToFileAsync(new RenderInput
{
    Project  = "billing",
    Template = "invoice",
    Data     = new Dictionary<string, object>
    {
        { "invoiceNumber", "INV-001" },
        { "total", 1280 },
    },
}, "./invoices/INV-001.pdf");

Console.WriteLine("Wrote ./invoices/INV-001.pdf");
