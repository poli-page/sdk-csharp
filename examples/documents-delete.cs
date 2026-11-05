// Demonstrates: client.Documents.DeleteAsync(id) — soft-deletes a stored document.
using PoliPage;

var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
});

await client.Documents.DeleteAsync("doc_abc123");

Console.WriteLine("Deleted doc_abc123");
