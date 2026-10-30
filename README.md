# Poli Page SDK for .NET

[![Nuget](https://img.shields.io/nuget/v/PoliPage?style=flat&logo=dotnet&logoColor=ffffff&label=Nuget)](https://www.nuget.org/packages/PoliPage)
[![Downloads](https://img.shields.io/nuget/dt/PoliPage?style=flat&logo=dotnet&logoColor=ffffff&label=Downloads)](https://www.nuget.org/packages/PoliPage)
[![Ci](https://img.shields.io/github/actions/workflow/status/poli-page/sdk-csharp/ci.yml?branch=main&style=flat&logo=githubactions&logoColor=ffffff&label=Ci)](https://github.com/poli-page/sdk-csharp/actions/workflows/ci.yml)
[![Codeql](https://img.shields.io/github/actions/workflow/status/poli-page/sdk-csharp/codeql.yml?branch=main&style=flat&logo=github&logoColor=ffffff&label=Codeql)](https://github.com/poli-page/sdk-csharp/actions/workflows/codeql.yml)
[![Coverage](https://img.shields.io/codecov/c/github/poli-page/sdk-csharp?style=flat&logo=codecov&logoColor=ffffff&label=Coverage)](https://codecov.io/github/poli-page/sdk-csharp)
[![.Net](https://img.shields.io/badge/.Net-8.0%20%7C%2010.0-blue?style=flat&logo=dotnet&logoColor=ffffff)](https://dotnet.microsoft.com/)
[![Deps](https://img.shields.io/badge/Deps-up%20to%20date-brightgreen?style=flat&logo=dotnet&logoColor=ffffff)](https://github.com/poli-page/sdk-csharp/network/dependencies)
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
  <PackageReference Include="PoliPage" Version="0.11.0" />
</ItemGroup>
```

Targets `net8.0` (LTS) and `net10.0` (LTS). Zero runtime dependencies beyond `Microsoft.Extensions.Logging.Abstractions` and the BCL.

## Quick start

Set `POLI_PAGE_API_KEY` to a `sandbox` key of a fresh organization (`pp_sandbox_…`, see [Authentication & environments](#authentication--environments)), then:

```csharp
using PoliPage;

var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
});

byte[] pdf = await client.Render.PdfAsync(new RenderInput
{
    Project  = "getting-started",
    Template = "welcome",
    Data     = new { name = "SDK Demo" },
});

await File.WriteAllBytesAsync("welcome.pdf", pdf);
// pdf is a byte[] of the rendered PDF document
```

Every organization gets a `getting-started` project, published into its `sandbox` environment when the organization is created, with a `welcome` template. A sandbox key renders it with no setup, so the snippet above runs as-is on a fresh organization. It is a normal, editable project: in an organization that renamed it, deleted it or removed it from `sandbox`, the API answers `404 PROJECT_NOT_FOUND` or `404 PROJECT_NOT_IN_ENVIRONMENT`, so use a fresh organization to follow along.

There is no version to pick: a render uses the project's copy published in the API key's environment. For your own templates, publish the project to the environment of the key you render with, then swap the slugs.

To preview a template or render inline HTML while you design it, use the playground: <https://play.poli.page>. The API and this SDK render published templates only.

### Try it locally — runnable demo

```bash
dotnet run --project samples/Demo
```

The demo renders `getting-started`/`welcome`, walks every public method end-to-end and writes the results to `samples/Demo/output/`. First run prompts for a `sandbox` key (`pp_sandbox_…`), checks its format and saves it to `.env` at the repo root; subsequent runs are silent. The `POLI_PAGE_API_KEY` env var wins over the file when set; `POLI_PAGE_BASE_URL` (optional) overrides the API host.

It walks nine steps: `Render.PdfAsync` → `Render.PdfStreamAsync` → `RenderToFileAsync` → `Render.DocumentAsync` → `Documents.GetAsync` → `Documents.ThumbnailsAsync` → `Documents.PreviewAsync` → `Documents.DeleteAsync` → `not-found` (a render of a template that does not exist, caught as `PoliPageNotFoundException` with code `TEMPLATE_NOT_FOUND`; the demo prints its code, status, message and request id).

### URL output — render and store, then download later

```csharp
DocumentDescriptor doc = await client.Render.DocumentAsync(new RenderInput
{
    Project  = "billing",
    Template = "invoice",
    Data     = new { invoiceNumber = "INV-001" },
});
// doc.DocumentId, doc.Environment, doc.PageCount, doc.SizeBytes, doc.PresignedPdfUrl
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
        poliPage.Render.PdfAsync(new RenderInput { /* … */ }, cancellationToken: ct);
}
```

## Working with stored documents

Every render produces a stored document, accessible via `DocumentId` for later download or thumbnails.

```csharp
// 1. Render and store
var doc = await client.Render.DocumentAsync(new RenderInput
{
    Project  = "billing",
    Template = "invoice",
    Data     = new { invoiceNumber = "INV-001" },
    Metadata = new RenderMetadata { ["customerId"] = "cust_123" },
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

// 5. Soft-delete when done (idempotent: deleting again also succeeds)
await client.Documents.DeleteAsync(doc.DocumentId);
```

The presigned URL has a ~15-minute TTL. If `DownloadPdfAsync` throws `PoliPageDownloadException`, call `Documents.GetAsync(id)` to refresh and retry.

## Authentication & environments

An API key belongs to exactly one environment of your organization, and its text names that environment: a key is `pp_{env}_{32 lowercase hex}`, matching

```text
^pp_(?=[a-z][a-z0-9-]{0,19}_)[a-z][a-z0-9]*(?:-[a-z0-9]+)*_[0-9a-f]{32}$
```

The API resolves the environment from the key and renders the project's copy published there; the SDK never sends an environment.

| Environment                           | Key            | Watermark | Counted |
| ------------------------------------- | -------------- | --------- | ------- |
| `sandbox` (every org)                 | `pp_sandbox_…` | yes       | no      |
| `live` (every org, deletable on Pro+) | `pp_live_…`    | no        | yes     |
| created on Pro+ (e.g. `staging`)      | `pp_staging_…` | no        | yes     |

"Counted" means the render counts toward the organization's monthly PDF quota. `sandbox` renders are watermarked, are not counted, and their documents expire 30 days after creation. There is no watermark option: a template that wants a watermark outside `sandbox` uses the `poli-watermark` class.

Standard and service keys share this one format. Pass the key through `PoliPageClientOptions.ApiKey` (the samples read it from `POLI_PAGE_API_KEY`; the SDK itself never reads the environment). Every key hits the same endpoint, `https://api.poli.page` by default or whatever you pass as `BaseUrl` (see [Configuration](#configuration)). The SDK passes the key through as a `Bearer` token and does not check its format.

## Methods

| Method                                                       | Returns                            | Description |
| ------------------------------------------------------------ | ---------------------------------- | ----------- |
| `client.Render.PdfAsync(input, opts?, ct)`                   | `Task<byte[]>`                     | Render a PDF, return bytes |
| `client.Render.PdfStreamAsync(input, opts?, ct)`             | `Task<Stream>`                     | Render and stream the response |
| `client.Render.DocumentAsync(input, opts?, ct)`              | `Task<DocumentDescriptor>`         | Render and return descriptor (skip auto-download) |
| `client.Documents.GetAsync(id, ct)`                          | `Task<DocumentDescriptor>`         | Retrieve a stored document |
| `client.Documents.PreviewAsync(id, ct)`                      | `Task<DocumentPreviewResult>`      | Stored document's paginated HTML |
| `client.Documents.ThumbnailsAsync(id, options?, ct)`         | `Task<IReadOnlyList<Thumbnail>>`   | Page thumbnails (PNG/JPEG/WebP, base64) |
| `client.Documents.DeleteAsync(id, ct)`                       | `Task`                             | Soft-delete a stored document (idempotent) |
| `descriptor.DownloadPdfAsync(ct)`                            | `Task<byte[]>`                     | Fetch bytes from the descriptor's presigned URL |
| `PoliPageClient.RenderToFileAsync(input, path, opts?, ct)`   | `Task`                             | Render and stream to disk |

Every render method takes a `RenderInput`: `Project`, `Template` and `Data` (required), plus optional `Metadata`, `Format`, `Orientation` and `Locale`. There is no version and no inline-HTML input; previews of a template and inline HTML live in the playground, <https://play.poli.page>. `Documents.PreviewAsync(id)` returns the stored HTML of a document you already rendered.

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
| `ApiKey`           | `string`                   | (required)               | API key, `pp_{env}_…` (e.g. `pp_sandbox_…`); it selects the environment |
| `BaseUrl`          | `Uri`                      | `https://api.poli.page`  | API base URL |
| `MaxRetries`       | `int`                      | `2`                      | Retry budget on top of the initial attempt |
| `RetryDelay`       | `TimeSpan`                 | `500ms`                  | Base exponential-backoff delay |
| `RequestTimeout`   | `TimeSpan`                 | `60s`                    | Per-attempt deadline: connection, headers and body (overridable per call) |
| `HttpClient`       | `HttpClient`               | new instance             | Inject custom transport / handlers |
| `DownloadHttpClient` | `HttpClient`             | new bare instance        | Presigned PDF downloads; its `Timeout` bounds the wait for the storage's headers only |
| `Logger`           | `ILogger<PoliPageClient>`  | `NullLogger`             | One DEBUG/attempt, WARN/retry, ERROR/terminal |
| `OnRetry`          | `Action<RetryEvent>`       | null                     | Fires before each retry sleep; `Attempt` is the attempt about to start (2 for the first retry) |
| `OnError`          | `Action<Exception>`        | null                     | Fires once with the error a call is about to throw |

## Error handling

The SDK throws `PoliPageException` for every failure: API errors, network failures, timeouts, a cancellation (`aborted`), download failures and invalid options. The base type carries `Code`, `StatusCode`, `Message`, `RequestId`, and `InnerException`. Subclasses route common cases for `catch`-based branching:

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
- `PoliPageAuthException` — 401 / 403. Covers `MissingApiKey`, `InvalidApiKey`, `Forbidden`, `OrganizationPaymentGrace`, `EnvironmentProtected`, `ThumbnailsNotAvailable`.
- `PoliPageNotFoundException` — 404. Covers `ProjectNotFound`, `ProjectNotInEnvironment`, `TemplateNotFound`, `DocumentNotFound`, `NotFound`.
- `PoliPageGoneException` — 410 `DocumentGone`. The document has been deleted.
- `PoliPageValidationException` — 400 / 422. Bad input payload, or a template that fails (`TemplateError`) or holds an injection attempt (`TemplateUnsafe`).
- `PoliPageRateLimitException` — 429 (`RateLimitExceeded`, `QuotaExceeded`, `OverageCapExceeded`). Exposes `RetryAfter`. Only `RateLimitExceeded` is retried.
- `PoliPageNetworkException` — DNS / TCP / TLS failure, or a connection closed before the whole answer (`NetworkError`, `Network request failed`). Retryable. A timed-out attempt is a base `PoliPageException` with `Code == PoliPageErrorCode.Timeout` (`Request timed out after <ms>ms`).
- `PoliPageDownloadException` — presigned URL fetch failed (`DownloadFailed`): the storage status (commonly 403 for an expired URL), or none for a network failure or timeout. Never retried.

Every subclass exposes the same `Code`, `StatusCode`, `Message`, and `RequestId` properties as the base type. Use `ex.Code` against the `PoliPageErrorCode` constants for fine-grained branching when the subclass is too coarse.

## Cancellation

Every async method accepts a `CancellationToken`. Pass one to abort a render in flight:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

var pdf = await client.Render.PdfAsync(input, cancellationToken: cts.Token);
```

When the token is canceled, the SDK throws `PoliPageException` with `Code == PoliPageErrorCode.Aborted` (`Request was aborted`, the `OperationCanceledException` as `InnerException`), at once, also during a retry wait or while the PDF is read; it is never retried. When an attempt of an API call times out, it is retried, then the SDK throws `PoliPageException` with `Code == PoliPageErrorCode.Timeout`.

The presigned PDF download that `Render.PdfAsync`, `Render.PdfStreamAsync`, `RenderToFileAsync` and `DocumentDescriptor.DownloadPdfAsync` run after the API call is not bounded by `RequestTimeout`. `DownloadHttpClient.Timeout` (100 seconds by default) bounds only the wait for the storage's response headers; if it expires, the SDK throws `PoliPageDownloadException` (`DownloadFailed`, `Failed to download PDF: network error`). Once the headers have arrived, reading the PDF body has no SDK time bound: pass a `CancellationToken` to bound it.

The default per-attempt timeout is 60 seconds, configurable via `PoliPageClientOptions.RequestTimeout` or per call via `RequestOptions.RequestTimeout`.

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

The SDK retries on **5xx**, **429**, **network errors**, and **timeouts**, except `QUOTA_EXCEEDED`, `OVERAGE_CAP_EXCEEDED` and `STORAGE_ACCESS_DENIED`, which waiting cannot fix. A `Retry-After` header (integer seconds or an HTTP-date, clamped to 0–30 s) sets the delay; otherwise the backoff is `RetryDelay × 2^(n-1)` with jitter in `[0.5, 1.5)`. The presigned download is never retried, and the HTTP stack never resends a request behind the SDK's back. Every POST sends an auto-generated `Idempotency-Key` (UUID v4), the same on every attempt; pass `RequestOptions.IdempotencyKey` to override:

```csharp
await client.Render.PdfAsync(input, new RequestOptions { IdempotencyKey = "inv-INV-001" });
```

The API does not deduplicate on `Idempotency-Key`: a render retried after a late 5xx, a `timeout` or a `network_error` can store a second document. Disable retries by setting `MaxRetries = 0` in `PoliPageClientOptions`.

## Type system

The SDK targets `net8.0` and `net10.0` with **nullable reference types** enabled. Every public symbol carries XML doc comments — they surface in IntelliSense and feed the auto-generated API reference.

- `RenderInput` is a sealed record with `required` members (`Project`, `Template`, `Data`), so a missing field is a compile-time error. Unset optional fields are omitted from the request body.
- Nullable wire fields use the BCL `?` annotation (`string? ProjectId`, `string? Locale`) so missing-vs-null is unambiguous in `System.Text.Json` round-trips.

Consumers should enable `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>` in their `.csproj` to surface the SDK's nullability contract.

## Concurrency & thread-safety

`PoliPageClient` is thread-safe and designed to be shared across the entire process. Register it as a singleton in DI (`AddPoliPage` does this automatically) and inject it everywhere; the underlying `HttpClient` pools connections and avoids socket exhaustion. **Never `new PoliPageClient(...)` per request** — that defeats connection pooling and is the most common .NET HTTP performance bug.

Parallel requests fan out cleanly via `Task.WhenAll`:

```csharp
var tasks = invoices.Select(inv =>
    client.Render.DocumentAsync(new RenderInput
    {
        Project  = "billing",
        Template = "invoice",
        Data     = inv.ToData(),
    }));

DocumentDescriptor[] results = await Task.WhenAll(tasks);
```

## Runtime support

Server-side only. The SDK runs on:

- **.NET 8 LTS** — supported through November 2026.
- **.NET 10 LTS** — supported through November 2028.

**Browsers (Blazor WebAssembly) are not supported.** API keys of every environment (`pp_{env}_…`) are secrets and must never be shipped to a browser. Call the SDK from your backend (ASP.NET Core, Worker Service, Azure Function, AWS Lambda) and proxy the result to the client. Blazor Server is fine because the SDK runs on the server.

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
