// Demonstrates: new PoliPageClient(options) — the SDK entry point.
using PoliPage;

using var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey         = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
    BaseUrl        = new Uri("https://api.poli.page"),
    RequestTimeout = TimeSpan.FromSeconds(30),
    MaxRetries     = 2,
    RetryDelay     = TimeSpan.FromMilliseconds(500),
});

Console.WriteLine("PoliPage client ready.");
