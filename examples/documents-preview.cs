// Demonstrates: client.Documents.PreviewAsync(id) — fetches the HTML preview and
// the page count of a stored document.
using PoliPage;

var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
});

DocumentPreviewResult preview = await client.Documents.PreviewAsync("doc_abc123");

Console.WriteLine($"{preview.PageCount} page(s), {preview.Html.Length} characters of HTML");
