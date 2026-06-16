# Poli Page SDK for .NET

[![Nuget](https://img.shields.io/nuget/v/PoliPage?style=flat&logo=dotnet&logoColor=ffffff&label=Nuget)](https://www.nuget.org/packages/PoliPage)
[![Downloads](https://img.shields.io/nuget/dt/PoliPage?style=flat&logo=dotnet&logoColor=ffffff&label=Downloads)](https://www.nuget.org/packages/PoliPage)
[![Ci](https://img.shields.io/github/actions/workflow/status/poli-page/sdk-csharp/ci.yml?branch=main&style=flat&logo=githubactions&logoColor=ffffff&label=Ci)](https://github.com/poli-page/sdk-csharp/actions/workflows/ci.yml)
[![Codeql](https://img.shields.io/github/actions/workflow/status/poli-page/sdk-csharp/codeql.yml?branch=main&style=flat&logo=github&logoColor=ffffff&label=Codeql)](https://github.com/poli-page/sdk-csharp/actions/workflows/codeql.yml)
[![Coverage](https://img.shields.io/codecov/c/github/poli-page/sdk-csharp?style=flat&logo=codecov&logoColor=ffffff&label=Coverage)](https://codecov.io/github/poli-page/sdk-csharp)
[![.Net](https://img.shields.io/badge/.Net-8.0%20%7C%2010.0-blue?style=flat&logo=dotnet&logoColor=ffffff)](https://dotnet.microsoft.com/)
[![Docs](https://img.shields.io/badge/Docs-online-brightgreen?style=flat&logo=readthedocs&logoColor=ffffff)](https://poli-page.github.io/sdk-csharp)
[![License](https://img.shields.io/badge/License-MIT-blue?style=flat&logo=gnu&logoColor=ffffff)](LICENSE)

Official .NET SDK for [Poli Page](https://poli.page) — render polished PDFs from HTML templates via the Poli Page API.

→ API reference (auto-generated from XML doc comments): **[poli-page.github.io/sdk-csharp](https://poli-page.github.io/sdk-csharp)**

## Install

```bash
dotnet add package PoliPage
```

Or add the reference directly to your `.csproj`:

```xml
<ItemGroup>
  <PackageReference Include="PoliPage" Version="1.0.0" />
</ItemGroup>
```

Targets `net8.0` (LTS) and `net10.0` (LTS). Zero runtime dependencies beyond `Microsoft.Extensions.Logging.Abstractions` and the BCL.

## Quick start

```csharp
using PoliPage;

var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
});

byte[] pdf = await client.Render.PdfAsync(new ProjectModeInput
{
    Project  = "getting-started",
    Template = "welcome",
    Version  = "1.0.0",
    Data     = new { name = "World" },
});

await File.WriteAllBytesAsync("welcome.pdf", pdf);
// pdf is a byte[] of the rendered PDF document
```

Every Poli Page org comes pre-provisioned with a `getting-started/welcome` template, so the snippet above runs as-is the moment you have an API key. Swap the slugs once you have pushed your own templates with the `poli` CLI.

### URL output — render and store, then download later

```csharp
DocumentDescriptor doc = await client.Render.DocumentAsync(new ProjectModeInput
{
    Project  = "billing",
    Template = "invoice",
    Version  = "1.0.0",
    Data     = new { invoiceNumber = "INV-001" },
});
// doc.DocumentId, doc.PageCount, doc.SizeBytes, doc.PresignedPdfUrl
```

### Dependency injection — register with `IServiceCollection`

```csharp
builder.Services.AddPoliPage(options =>
{
    options.ApiKey = builder.Configuration["PoliPage:ApiKey"]!;
});

// Inject as PoliPageClient anywhere
public class InvoiceService(PoliPageClient poliPage)
{
    public Task<byte[]> RenderAsync(Invoice invoice, CancellationToken ct) =>
        poliPage.Render.PdfAsync(new ProjectModeInput { /* … */ }, cancellationToken: ct);
}
```

## Working with stored documents

Every render produces a stored document, accessible via `DocumentId` for later download or thumbnails.

```csharp
// 1. Render and store
var doc = await client.Render.DocumentAsync(new ProjectModeInput
{
    Project  = "billing",
    Template = "invoice",
    Version  = "1.0.0",
    Data     = new { invoiceNumber = "INV-001" },
    Metadata = new Dictionary<string, object> { ["customerId"] = "cust_123" },
});

// 2. Persist the document ID
await db.Invoices.UpdateAsync("INV-001", doc.DocumentId);

// 3. Later, fetch a fresh presigned URL + download
var fresh = await client.Documents.GetAsync(doc.DocumentId);
byte[] pdf = await fresh.DownloadPdfAsync();

// 4. Generate thumbnails (Starter+ tier)
IReadOnlyList<Thumbnail> thumbs = await client.Documents.ThumbnailsAsync(
    doc.DocumentId,
    new ThumbnailOptions { Width = 320, Format = ThumbnailFormat.Png });

// 5. Soft-delete when done
await client.Documents.DeleteAsync(doc.DocumentId);
```

The presigned URL has a ~15-minute TTL. If `DownloadPdfAsync` throws `PoliPageDownloadException`, call `Documents.GetAsync(id)` to refresh and retry.

## Authentication & environments

The API key is read from the `POLI_PAGE_API_KEY` environment variable by default, or passed via `PoliPageClientOptions.ApiKey`. The mode is determined by the key prefix:

| Prefix       | Mode                                                               |
| ------------ | ------------------------------------------------------------------ |
| `pp_test_…`  | Sandbox — not billed, generous rate limits                          |
| `pp_live_…`  | Live — billed, production rate limits                              |
| `pp_sa_…`    | Service-account key; environment matches the SA's configuration    |

All prefixes hit the same endpoint (`https://api.poli.page`). The SDK passes the key as a Bearer token and never inspects the prefix.

## Methods

| Method                                                       | Returns                            | Description |
| ------------------------------------------------------------ | ---------------------------------- | ----------- |
| `client.Render.PdfAsync(input, opts?, ct)`                   | `Task<byte[]>`                     | Render a PDF, return bytes |
| `client.Render.PdfStreamAsync(input, opts?, ct)`             | `Task<Stream>`                     | Render and stream the response |
| `client.Render.PreviewAsync(input, opts?, ct)`               | `Task<PreviewResult>`              | Paginated HTML preview |
| `client.Render.DocumentAsync(input, opts?, ct)`              | `Task<DocumentDescriptor>`         | Render and return descriptor (skip auto-download) |
| `client.Documents.GetAsync(id, ct)`                          | `Task<DocumentDescriptor>`         | Retrieve a stored document |
| `client.Documents.PreviewAsync(id, ct)`                      | `Task<DocumentPreviewResult>`      | Stored document's paginated HTML |
| `client.Documents.ThumbnailsAsync(id, options, ct)`          | `Task<IReadOnlyList<Thumbnail>>`   | Page thumbnails (PNG/JPEG, base64) |
| `client.Documents.DeleteAsync(id, ct)`                       | `Task`                             | Soft-delete a stored document |
| `descriptor.DownloadPdfAsync(ct)`                            | `Task<byte[]>`                     | Fetch bytes from the descriptor's presigned URL |
| `PoliPageClient.RenderToFileAsync(input, path, opts?, ct)`   | `Task`                             | Render and stream to disk |

`Render.PdfAsync`, `PdfStreamAsync`, and `DocumentAsync` accept `ProjectModeInput` directly — passing `InlineModeInput` is a compile-time error. `Render.PreviewAsync` accepts the sealed `RenderInput` base type, satisfied by both modes.

## Configuration

All options live on `PoliPageClientOptions` and are passed to the constructor or the `AddPoliPage` extension. Per-call options (`IdempotencyKey`, `RequestTimeout`, `Headers`) are passed to individual methods as the optional `RequestOptions` argument.

```csharp
var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey         = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
    BaseUrl        = new Uri("https://api.poli.page"),
    MaxRetries     = 2,
    RetryDelay     = TimeSpan.FromMilliseconds(500),
    RequestTimeout = TimeSpan.FromSeconds(60),
    HttpClient     = httpClientFactory.CreateClient("poli-page"),
    Logger         = loggerFactory.CreateLogger<PoliPageClient>(),
    OnRetry        = evt => metrics.Counter("poli.retry").Add(1, ("attempt", evt.Attempt)),
    OnError        = ex  => sentry.CaptureException(ex),
});

// Per-call overrides
var pdf = await client.Render.PdfAsync(input, new RequestOptions
{
    IdempotencyKey = "inv-INV-001",
    RequestTimeout = TimeSpan.FromSeconds(5),
    Headers        = { ["X-Trace-Id"] = traceId },
});
```

| Option            | Type                       | Default                  | Description |
| ----------------- | -------------------------- | ------------------------ | ----------- |
| `ApiKey`           | `string`                   | (required)               | `pp_test_*`, `pp_live_*`, or `pp_sa_*` API key |
| `BaseUrl`          | `Uri`                      | `https://api.poli.page`  | API base URL |
| `MaxRetries`       | `int`                      | `2`                      | Retry budget on top of the initial attempt |
| `RetryDelay`       | `TimeSpan`                 | `500ms`                  | Base exponential-backoff delay |
| `RequestTimeout`   | `TimeSpan`                 | `60s`                    | Per-request deadline (overridable per call) |
| `HttpClient`       | `HttpClient`               | new instance             | Inject custom transport / handlers |
| `Logger`           | `ILogger<PoliPageClient>`  | `NullLogger`             | One DEBUG/attempt, WARN/retry, ERROR/terminal |
| `OnRetry`          | `Action<RetryEvent>`       | null                     | Fires before each retry sleep |
| `OnError`          | `Action<Exception>`        | null                     | Fires on terminal failure |

## Error handling

The SDK throws `PoliPageException` for every API failure. The base type carries `Code`, `StatusCode`, `Message`, `RequestId`, and `InnerException`. Subclasses route common cases for `catch`-based branching:

```csharp
try
{
    var pdf = await client.Render.PdfAsync(input);
}
catch (PoliPageAuthException)          { await RefreshCredentialsAsync(); }
catch (PoliPageRateLimitException ex)  { await QueueForLaterAsync(ex.RetryAfter); }
catch (PoliPageNotFoundException)      { return NotFound(); }
catch (PoliPageValidationException ex) { return BadRequest(ex.Message); }
catch (PoliPageException ex)
{
    logger.LogError(ex, "Poli Page error: code={Code} status={Status} requestId={RequestId}",
        ex.Code, ex.StatusCode, ex.RequestId);
    throw;
}
```

Exception hierarchy:

- `PoliPageException` — base type, thrown on any API failure.
- `PoliPageAuthException` — 401 / 403. Covers `Unauthorized`, `Forbidden`.
- `PoliPageNotFoundException` — 404. Covers `NotFound`, `VersionNotFound`, `DocumentNotFound`.
- `PoliPageGoneException` — 410. Document was soft-deleted.
- `PoliPageValidationException` — 400 / 422. Bad input payload.
- `PoliPageRateLimitException` — 429. Exposes `RetryAfter`.
- `PoliPagePaymentRequiredException` — 402. Subscription has unpaid invoices.
- `PoliPageNetworkException` — DNS / TCP / TLS / timeout. Retryable.
- `PoliPageDownloadException` — presigned S3 URL fetch failed (commonly URL expiry).

Every subclass exposes the same `Code`, `StatusCode`, `Message`, and `RequestId` properties as the base type. Use `ex.Code` against the `PoliPageErrorCode` constants for fine-grained branching when the subclass is too coarse.

## Cancellation

Every async method accepts a `CancellationToken`. Pass one to abort a render in flight:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

var pdf = await client.Render.PdfAsync(input, cancellationToken: cts.Token);
```

When the token is canceled, the SDK throws `OperationCanceledException` (or `TaskCanceledException`); when the per-request timeout expires, it throws `PoliPageException` with `Code == PoliPageErrorCode.Timeout`. Caller-aborted operations are never retried.

The default per-request timeout is 60 seconds, configurable via `PoliPageClientOptions.RequestTimeout` or per call via `RequestOptions.RequestTimeout`.

## Observability

The SDK integrates with `Microsoft.Extensions.Logging.ILogger<PoliPageClient>`:

```csharp
var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
    Logger = loggerFactory.CreateLogger<PoliPageClient>(),
});
```

One DEBUG entry per HTTP attempt, one WARN per retry, one ERROR per terminal failure. The `Authorization` header is never logged.

For SDK-level events, register hooks:

```csharp
new PoliPageClientOptions
{
    OnRetry = evt   => metrics.Counter("poli.retry").Add(1, ("attempt", evt.Attempt)),
    OnError = error => sentry.CaptureException(error),
};
```

Hooks are synchronous, optional, and exception-safe — a hook that throws never breaks the request. For full HTTP-level inspection (headers + body), pass a custom `HttpClient` configured with a `DelegatingHandler`:

```csharp
var handler = new TracingHandler { InnerHandler = new SocketsHttpHandler() };
var http    = new HttpClient(handler);

var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey     = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
    HttpClient = http,
});
```

The SDK emits one OpenTelemetry activity per HTTP attempt under the source name `PoliPage` — register it on your `TracerProviderBuilder` to capture spans automatically.

## Retries & idempotency

The SDK retries on **5xx**, **429**, **network errors**, and **timeouts**. Backoff is exponential (`RetryDelay × 2^N`) with jitter in `[0.5, 1.5)`, capped at the server's `Retry-After` header when provided (max 30s). Every POST sends an auto-generated `Idempotency-Key` (UUID v4); pass `RequestOptions.IdempotencyKey` to override:

```csharp
await client.Render.PdfAsync(input, new RequestOptions { IdempotencyKey = "inv-INV-001" });
```

Disable retries by setting `MaxRetries = 0` in `PoliPageClientOptions`.

## Type system

The SDK targets `net8.0` and `net10.0` with **nullable reference types** enabled. Every public symbol carries XML doc comments — they surface in IntelliSense and feed the auto-generated API reference.

- Input types (`ProjectModeInput`, `InlineModeInput`) are sealed records satisfying the abstract `RenderInput` base type. External types cannot extend it.
- Methods that require project mode (`PdfAsync`, `PdfStreamAsync`, `DocumentAsync`) accept `ProjectModeInput` directly — passing `InlineModeInput` is a compile-time error.
- Nullable wire fields use the BCL `?` annotation (`string? ProjectId`, `string? Version`) so missing-vs-null is unambiguous in `System.Text.Json` round-trips.

Consumers should enable `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in their `.csproj` to surface the SDK's nullability contract.

## Concurrency & thread-safety

`PoliPageClient` is thread-safe and designed to be shared across the entire process. Register it as a singleton in DI (`AddPoliPage` does this automatically) and inject it everywhere; the underlying `HttpClient` pools connections and avoids socket exhaustion. **Never `new PoliPageClient(...)` per request** — that defeats connection pooling and is the most common .NET HTTP performance bug.

Parallel requests fan out cleanly via `Task.WhenAll`:

```csharp
var tasks = invoices.Select(inv =>
    client.Render.DocumentAsync(new ProjectModeInput
    {
        Project  = "billing",
        Template = "invoice",
        Version  = "1.0.0",
        Data     = inv.ToData(),
    }));

DocumentDescriptor[] results = await Task.WhenAll(tasks);
```

## Runtime support

Server-side only. The SDK runs on:

- **.NET 8 LTS** — supported through November 2026.
- **.NET 10 LTS** — supported through November 2028.

**Browsers (Blazor WebAssembly) are not supported.** API keys (`pp_test_*`, `pp_live_*`) are secrets and must never be shipped to a browser. Call the SDK from your backend (ASP.NET Core, Worker Service, Azure Function, AWS Lambda) and proxy the result to the client. Blazor Server is fine because the SDK runs on the server.

CI exercises `net8.0` and `net10.0` on Linux, plus `net10.0` on Windows and macOS.

## Requirements

- .NET 8 LTS or .NET 10 LTS.
- Outbound HTTPS to `api.poli.page` and the presigned S3 hosts the API returns for downloads.
- No other system dependencies — pure managed code.

## Documentation & support

- Platform docs: [docs.poli.page](https://docs.poli.page)
- SDK API reference: [poli-page.github.io/sdk-csharp](https://poli-page.github.io/sdk-csharp)
- Sign up & generate API keys: [app.poli.page](https://app.poli.page)
- Issues: [github.com/poli-page/sdk-csharp/issues](https://github.com/poli-page/sdk-csharp/issues)
- Support: support@poli.page

## License

[MIT](LICENSE) © Poli Page
