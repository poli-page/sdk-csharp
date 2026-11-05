// Demonstrates: client.Documents.GetAsync(id) — retrieves a stored document and
// refreshes its presigned PDF URL.
using PoliPage;

var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
});

DocumentDescriptor doc = await client.Documents.GetAsync("doc_abc123");

Console.WriteLine($"{doc.DocumentId}: {doc.PageCount} page(s)");
Console.WriteLine($"Fresh URL: {doc.PresignedPdfUrl}");
