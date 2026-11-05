// Demonstrates: client.Documents.ThumbnailsAsync(id, thumbnailOptions) — renders
// page thumbnails of a stored document.
using PoliPage;

var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
});

IReadOnlyList<Thumbnail> thumbnails = await client.Documents.ThumbnailsAsync(
    "doc_abc123",
    new ThumbnailOptions
    {
        Width  = 480,
        Format = ThumbnailFormat.Png,
        Pages  = new[] { 1 },
    });

foreach (Thumbnail t in thumbnails)
{
    byte[] bytes = Convert.FromBase64String(t.Data);
    Console.WriteLine($"Page {t.Page}: {t.ContentType} {t.Width}x{t.Height} ({bytes.Length} bytes)");
}
