# Poli Page SDK for .NET — Implementation Plan

**Created**: 2026-05-29
**Author**: handoff doc, not a spec — the spec lives at `/Users/mickael/Projects/sdk-node/sdk-specification.md` v1.3
**Reference impl**: `/Users/mickael/Projects/sdk-node` (`@poli-page/sdk` v1.0, shipped). See also `/Users/mickael/Projects/sdk-go/sdk-go-plan.md` and `/Users/mickael/Projects/sdk-python/sdk-python-plan.md` for the statically-typed siblings.
**Empirical API source of truth**: the CLI's api-client at `/Users/mickael/n/lib/node_modules/@poli-page/cli/dist/api-client.{js,d.ts}` — works end-to-end against `api-develop.poli.page`. When spec and deployed API disagree, the CLI's behavior wins.

This document is the briefing for a fresh Claude session in the new `sdk-csharp` repo. Everything a new conversation needs to start work — links to source-of-truth files, design decisions already made, the build order, and the open questions — is captured here.

---

## 0. TL;DR

We are shipping the .NET sibling of `@poli-page/sdk`. The contract is fixed (`sdk-specification.md` v1.3); we are translating an already-shipped TypeScript reference implementation into idiomatic C#. The published package is `PoliPage` on NuGet; the root namespace is `PoliPage`.

The plan: single multi-target NuGet package (`net8.0` + `net10.0`) with `<Nullable>enable</Nullable>` and `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`. One async `PoliPageClient` with `*Async` methods accepting `CancellationToken` (the BCL idiom — no blocking variants in v1, follow Stripe.NET / Azure SDK precedent). Options object pattern (`PoliPageClientOptions`) for client config; per-call options via an optional `RequestOptions` parameter. Built on `System.Net.Http.HttpClient` with a `DelegatingHandler`-based retry/idempotency layer; `System.Text.Json` (source-generated where the perf matters) for wire serialization; `Microsoft.Extensions.Logging.ILogger<PoliPageClient>` for observability; `System.Diagnostics.ActivitySource` for OpenTelemetry. Exception hierarchy rooted at `PoliPageException` with specific subclasses for the common 4xx/5xx classes. DI integration via `Microsoft.Extensions.DependencyInjection.AddPoliPage(...)` extension method that wires up `IHttpClientFactory`. xUnit + FluentAssertions + WireMock.Net for tests. Samples shipped as a `samples/` solution folder. CI on GitHub Actions across **net8.0 + net10.0** on Ubuntu, plus a single net10.0 job on Windows and macOS — Microsoft's own LTS support window. Manual local pre-flight via `scripts/publish.sh`; `dotnet nuget push` to publish.

**No NuGet auto-publish.** Per the engineering guide §6.4 there MUST NOT be a CI workflow that publishes on tag push without manual intervention. We mirror Stripe.NET's pattern: a `workflow_dispatch`-gated release workflow with the maintainer confirming publish in the GitHub UI, plus a local script for offline fallback.

Ship in 8 phases (see §13) — same shape as the Go sibling plus one extra phase for DI integration and source-generated JSON wiring. Target: feature-parity 1.0.0 release, behavior-identical to `@poli-page/sdk@1.0.0`. **"Behavior parity" specifically means: same retry policy (5xx+429+network+timeout, jitter `[0.5,1.5)`, Retry-After cap 30s), same error codes round-tripped verbatim, same `PoliPageAuthException` covering 401+403, same constructor validation, same hooks-never-break-the-request, same project-mode-only constraint on `Render.PdfAsync` / `Render.PdfStreamAsync` / `Render.DocumentAsync`, same primitive-only `Metadata`, same thumbnails wire wrap/unwrap, same `Documents.PreviewAsync` text/html + `X-Document-Page-Count` parsing.**

---

## 1. Source-of-truth references

Read these in order before writing a single line of code:

1. **Multi-language spec** — `/Users/mickael/Projects/sdk-node/sdk-specification.md` (v1.3). Defines the contract every SDK must meet: methods, options, errors, retry policy, HTTP rules, tier requirements.
2. **SDK engineering guide** — `/Users/mickael/Projects/sdk-node/sdk-engineering-guide.md`. Cross-SDK policy: versioning, CHANGELOG/MIGRATION discipline, CI gates, language-version matrices, release flow, pre-push hooks. **Authoritative** — when this plan and the engineering guide disagree on conventions, the engineering guide wins.
3. **SDK README convention** — `/Users/mickael/Projects/SDK_README_CONVENTION.md`. The 16-H2 structure every SDK README MUST follow.
4. **SDK roadmap** — `/Users/mickael/Projects/sdk-node/sdk-roadmap.md` v1.3. Multi-repo strategy across all SDKs and the 12-repo target.
5. **Node SDK source** — `/Users/mickael/Projects/sdk-node/src/`. Reference implementation. When the spec is silent, the Node SDK's behavior is canonical (spec §11).
6. **Node SDK tests** — `/Users/mickael/Projects/sdk-node/tests/`. Especially `tests/integration/` for what the deployed API actually returns, and `tests/error-codes.test.ts` for the full error matrix.
7. **Go sibling plan** — `/Users/mickael/Projects/sdk-go/sdk-go-plan.md`. Shares every architectural decision for a statically-typed sync client; when in doubt, mirror it with C# substitutions. The .NET-specific divergences are catalogued in §18 of this doc.
8. **CLI api-client** — `/Users/mickael/n/lib/node_modules/@poli-page/cli/dist/api-client.{js,d.ts}`. Empirical source of truth for the deployed API. If the spec and the CLI disagree, the CLI is right.

Anytime you're unsure about a behavior, check the Node SDK source. If the Node SDK behavior is wrong, that's a separate bug — fix it in `sdk-node` first, then port; don't diverge silently.

---

## 2. Naming and identity

Per spec §2:

| Field | Value |
|---|---|
| **NuGet package** | `PoliPage` |
| **Root namespace** | `PoliPage` |
| **Client type** | `PoliPageClient` (constructed via `new PoliPageClient(PoliPageClientOptions)` or `services.AddPoliPage(...)`) |
| **Method casing** | PascalCase + `Async` suffix (BCL idiom) — `Render.PdfAsync`, `Render.PdfStreamAsync`, `Render.PreviewAsync`, `Render.DocumentAsync`, `Documents.GetAsync`, `Documents.PreviewAsync`, `Documents.ThumbnailsAsync`, `Documents.DeleteAsync` |
| **Exception base** | `PoliPageException` (mirrors Node's `PoliPageError`) |
| **File helper** | `PoliPageClient.RenderToFileAsync(input, path, …)` — static method on the client class (no top-level functions in C#) |
| **Package version** | NuGet semver; start at `1.0.0` |
| **Assembly version** | `<Version>` in `Directory.Build.props`, bumped manually on each release. Embedded into the `User-Agent` header. |
| **User-Agent** | `poli-page-sdk-dotnet/<version>` |

Field names on input / option records follow PascalCase. The wire JSON uses camelCase via `[JsonPropertyName("projectSlug")]` attributes (or a global `JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase`). The SDK uses `System.Text.Json` — prefer source-generated `JsonSerializerContext` for AOT-friendliness once the surface stabilises.

---

## 3. Architecture

### 3.1 Solution layout

```
sdk-csharp/
├── Directory.Build.props            # <Version>, <Nullable>, <TreatWarningsAsErrors>, common metadata
├── Directory.Packages.props         # Central Package Management — pinned versions
├── PoliPage.sln
├── .editorconfig                    # dotnet format + analyzer rules
├── global.json                      # SDK pin (10.0.x)
├── src/
│   └── PoliPage/
│       ├── PoliPage.csproj          # multi-target net8.0;net10.0
│       ├── PoliPageClient.cs        # Client, ctor, namespace fields, RenderToFileAsync helper
│       ├── PoliPageClientOptions.cs # ApiKey, BaseUrl, MaxRetries, RetryDelay, RequestTimeout, HttpClient, Logger, OnRetry, OnError
│       ├── RequestOptions.cs        # per-call: IdempotencyKey, RequestTimeout, Headers
│       ├── RenderNamespace.cs       # Render.PdfAsync, PdfStreamAsync, PreviewAsync, DocumentAsync
│       ├── DocumentsNamespace.cs    # Documents.GetAsync, PreviewAsync, ThumbnailsAsync, DeleteAsync
│       ├── Inputs/
│       │   ├── RenderInput.cs       # sealed abstract base
│       │   ├── ProjectModeInput.cs  # record : RenderInput
│       │   └── InlineModeInput.cs   # record : RenderInput
│       ├── Models/
│       │   ├── DocumentDescriptor.cs
│       │   ├── PreviewResult.cs
│       │   ├── DocumentPreviewResult.cs
│       │   ├── Thumbnail.cs
│       │   ├── ThumbnailOptions.cs
│       │   ├── RenderMetadata.cs
│       │   └── RetryEvent.cs
│       ├── Exceptions/
│       │   ├── PoliPageException.cs           # base — Code, StatusCode, RequestId
│       │   ├── PoliPageAuthException.cs       # 401 + 403
│       │   ├── PoliPageNotFoundException.cs   # 404 (incl. VersionNotFound, DocumentNotFound)
│       │   ├── PoliPageGoneException.cs       # 410
│       │   ├── PoliPageValidationException.cs # 400 + 422
│       │   ├── PoliPageRateLimitException.cs  # 429 — RetryAfter
│       │   ├── PoliPagePaymentRequiredException.cs # 402
│       │   ├── PoliPageNetworkException.cs    # DNS/TCP/TLS/timeout
│       │   └── PoliPageDownloadException.cs   # presigned S3 fetch
│       ├── PoliPageErrorCode.cs               # static class with code constants
│       ├── DependencyInjection/
│       │   └── ServiceCollectionExtensions.cs # AddPoliPage(this IServiceCollection, …)
│       ├── Internal/
│       │   ├── Transport/
│       │   │   ├── ITransport.cs              # internal seam — PostAsync, GetAsync, DeleteAsync
│       │   │   ├── HttpTransport.cs           # default impl wrapping HttpClient
│       │   │   ├── RetryHandler.cs            # DelegatingHandler — backoff + jitter + Retry-After
│       │   │   ├── IdempotencyHandler.cs      # DelegatingHandler — auto UUID per POST
│       │   │   └── ErrorMappingHandler.cs     # DelegatingHandler — non-2xx → PoliPage*Exception
│       │   ├── Backoff.cs                     # PURE — ComputeBackoff(attempt, baseDelay), jitter, cap
│       │   ├── Headers.cs                     # PURE — BuildHeaders, ParseRetryAfter
│       │   ├── Urls.cs                        # PURE — BuildUrl, path templates
│       │   ├── ErrorParsing.cs                # PURE — wire → PoliPage*Exception mapping
│       │   ├── Uuid.cs                        # `Guid.NewGuid().ToString()` wrapper — kept here for testability
│       │   └── VersionInfo.cs                 # const Version used for User-Agent
│       └── PoliPage.csproj
├── samples/
│   ├── Demo/                       # Runnable demo — NOT a release artifact. `dotnet run --project samples/Demo`
│   │   ├── Demo.csproj
│   │   └── Program.cs
│   ├── AspNetCore.MinimalApi/
│   ├── WorkerService/
│   ├── AzureFunctions.Isolated/
│   └── AwsLambda/
├── tests/
│   ├── PoliPage.Tests/             # xUnit unit tests
│   │   ├── PoliPage.Tests.csproj
│   │   ├── PoliPageClientTests.cs
│   │   ├── RenderNamespaceTests.cs
│   │   ├── DocumentsNamespaceTests.cs
│   │   ├── ErrorMappingTests.cs
│   │   ├── RetryHandlerTests.cs
│   │   └── DependencyInjectionTests.cs
│   └── PoliPage.IntegrationTests/  # gated: [Trait("Category", "Integration")]
│       └── EndToEndTests.cs
├── testdata/
│   └── templates/invoice/          # Copied from sdk-node/demo/templates/ for cross-lang byte-diffability
├── .github/
│   ├── dependabot.yml              # nuget + github-actions, weekly schedule (engineering guide §4.12)
│   └── workflows/
│       ├── ci.yml                  # restore + format + build + test + pack + install-smoke on net8.0,net10.0
│       ├── integration.yml         # nightly + push-to-main; hits develop API
│       ├── codeql.yml              # CodeQL language: csharp (engineering guide §4.11)
│       └── release.yml             # workflow_dispatch — gated by environment with required reviewers
├── scripts/
│   ├── publish.sh                  # primary local publishing path: format + test + pack + push + tag
│   └── install-hooks.sh            # writes .git/hooks/pre-push
├── README.md
├── CHANGELOG.md                    # keepachangelog format, mirror sdk-node
├── MIGRATION.md
├── SECURITY.md
├── CONTRIBUTING.md
├── LICENSE                         # MIT
├── CLAUDE.md                       # this repo's CLAUDE.md
├── sdk-engineering-guide.md        # copy of the shared cross-SDK engineering guide
├── sdk-roadmap.md                  # copy of the shared roadmap (v1.3 — includes .NET as P5)
└── sdk-csharp-plan.md              # this document
```

Reasoning: the **transport core** (URL/header building, retry math, error mapping) is pure functions in `Internal/`. C#'s `internal` keyword + `[InternalsVisibleTo("PoliPage.Tests")]` is the visibility boundary. The public `PoliPageClient` orchestrates retries via a chain of `DelegatingHandler`s on the `HttpClient` — the BCL-idiomatic place to put retry, idempotency-key injection, and error mapping. Namespace types (`Render`, `Documents`) hold an `ITransport` reference and don't know about HTTP details.

This mirrors Node's `internal/http.ts` (pure) + `index.ts` (orchestration) + `render.ts`/`documents.ts` (namespace impls) split. Same architecture, different idioms — the `DelegatingHandler` chain is the BCL-native equivalent of Node's transport middleware.

### 3.2 The transport seam

```csharp
// internal seam — not exported. HttpTransport implements this.
internal interface ITransport
{
    Task<HttpResponseMessage> PostAsync(string path, object body, string idempotencyKey, RequestOptions? opts, CancellationToken ct);
    Task<HttpResponseMessage> GetAsync(string path, RequestOptions? opts, CancellationToken ct);
    Task DeleteAsync(string path, RequestOptions? opts, CancellationToken ct);
}

public sealed class Render { internal Render(ITransport t) { … } }
public sealed class Documents { internal Documents(ITransport t) { … } }
```

`HttpTransport` wraps `HttpClient`. Render and Documents namespaces hold an `ITransport` reference. This is the seam unit tests use to inject a fake transport without spinning up a `WireMock.Net` server. Public callers never see the interface.

**Per the Node SDK's n2 design memory**: design all four verbs from day one. Don't repeat the Node mistake of building a POST-only request method and needing to widen it for GET/DELETE later.

### 3.3 Public surface

```csharp
using PoliPage;

var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey     = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
    MaxRetries = 3,
});

try
{
    byte[] pdf = await client.Render.PdfAsync(new ProjectModeInput
    {
        Project  = "billing",
        Template = "invoice",
        Version  = "1.0.0",
        Data     = new { invoiceNumber = "INV-001" },
    },
    new RequestOptions { IdempotencyKey = "inv-INV-001" });

    await File.WriteAllBytesAsync("invoice.pdf", pdf);
}
catch (PoliPageAuthException)
{
    // refresh credentials
}
catch (PoliPageException ex)
{
    logger.LogError(ex,
        "Poli Page error: code={Code} status={Status} requestId={RequestId}",
        ex.Code, ex.StatusCode, ex.RequestId);
    throw;
}
```

The package exports:
- `PoliPageClient`, `PoliPageClientOptions`, `RequestOptions`
- `Render`, `Documents` — namespace types reachable as properties on `PoliPageClient`
- `RenderInput` (sealed abstract), `ProjectModeInput`, `InlineModeInput`, `RenderMetadata` (typed alias over `Dictionary<string, object>`)
- `DocumentDescriptor`, `DocumentPreviewResult`, `PreviewResult`, `Thumbnail`, `ThumbnailOptions`, `ThumbnailFormat`
- `PoliPageException` + the 8 subclasses + `PoliPageErrorCode` constants
- `RetryEvent` for the `OnRetry` hook
- `ServiceCollectionExtensions.AddPoliPage` for DI registration

### 3.4 DocumentDescriptor and DownloadPdfAsync

Mirrors Node's `attachDownloadPdf` (`render.ts:40-63` + `documents.ts:21-44`). The descriptor exposes a `DownloadPdfAsync(CancellationToken)` method.

Two design pitfalls drive the shape below:

1. **Presigned S3 URLs reject reused auth.** If we send the S3 GET through the SDK's own `HttpClient`, our `Authorization` / `X-API-Key` and the SDK `User-Agent` ride along on every request. S3 returns `403 SignatureDoesNotMatch` when an unexpected `Authorization` header is present alongside a presigned query signature; some object stores additionally reject on UA. The download MUST go through a **header-less transport** that is distinct from the API pipeline.
2. **`System.Text.Json` cannot populate `internal init` members from the wire.** The descriptor is deserialized from the API response; the namespace method that builds it then copies the deserialized instance via `with { Downloader = … }` before handing it to the caller. The `[JsonIgnore]` is load-bearing — without it, serialization round-trips drop the closure on `null`.

The downloader is an internal `Func<string, CancellationToken, Task<byte[]>>` injected by the parent `PoliPageClient`. It closes over a **separate, dedicated `HttpClient`** with no default headers and no `DelegatingHandler` chain:

```csharp
public sealed record DocumentDescriptor
{
    [JsonPropertyName("documentId")]      public required string DocumentId      { get; init; }
    [JsonPropertyName("organizationId")]  public required string OrganizationId  { get; init; }
    [JsonPropertyName("projectId")]       public string? ProjectId               { get; init; }
    [JsonPropertyName("projectSlug")]     public string? ProjectSlug             { get; init; }
    [JsonPropertyName("templateId")]      public string? TemplateId              { get; init; }
    [JsonPropertyName("templateSlug")]    public string? TemplateSlug            { get; init; }
    [JsonPropertyName("version")]         public string? Version                 { get; init; }
    [JsonPropertyName("environment")]     public required string Environment     { get; init; }
    [JsonPropertyName("format")]          public required string Format          { get; init; }
    [JsonPropertyName("pageCount")]       public required int PageCount          { get; init; }
    [JsonPropertyName("sizeBytes")]       public required long SizeBytes         { get; init; }
    [JsonPropertyName("presignedPdfUrl")] public required string PresignedPdfUrl { get; init; }
    [JsonPropertyName("metadata")]        public RenderMetadata? Metadata        { get; init; }
    // … remaining fields per spec §4.

    [JsonIgnore]
    internal Func<string, CancellationToken, Task<byte[]>>? Downloader { get; init; }

    public Task<byte[]> DownloadPdfAsync(CancellationToken cancellationToken = default)
    {
        if (Downloader is null)
            throw new InvalidOperationException(
                "DocumentDescriptor was not produced by a PoliPageClient — DownloadPdfAsync requires the SDK-injected downloader.");
        return Downloader(PresignedPdfUrl, cancellationToken);
    }
}
```

`PoliPageClient` owns a dedicated `HttpClient _downloadHttp` (the `"PoliPage.Download"` named client in DI mode, a directly-constructed `HttpClient` in non-DI mode). Its `DefaultRequestHeaders` are never mutated, so `Authorization` and `User-Agent` cannot leak into the presigned GET:

```csharp
internal async Task<byte[]> DownloadAsync(string url, CancellationToken ct)
{
    using var req = new HttpRequestMessage(HttpMethod.Get, url);
    using var resp = await _downloadHttp
        .SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
        .ConfigureAwait(false);

    if (!resp.IsSuccessStatusCode)
    {
        var requestId = resp.Headers.TryGetValues("X-Request-Id", out var v) ? v.FirstOrDefault() : null;
        throw new PoliPageDownloadException(
            PoliPageErrorCode.DownloadFailed,
            (int)resp.StatusCode,
            $"Presigned download failed: HTTP {(int)resp.StatusCode}",
            requestId);
    }

    return await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
}
```

Namespace methods compose: deserialize the response → `descriptor with { Downloader = _client.DownloadAsync }` → return to caller. External callers cannot set `Downloader` (it's `internal init`), so a manually-constructed descriptor throws `InvalidOperationException` on `DownloadPdfAsync` — the explicit failure mode Node mirrors with its "no owner" guard.

---

## 4. Wire serialization (System.Text.Json)

Per spec §6:

- Request bodies: PascalCase C# properties + `[JsonPropertyName("camelCase")]` attributes, OR global `JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase` — pick one and be consistent. Recommendation: rely on the global policy for the simple types, attribute the few fields where the C# name and wire name diverge (`PageCount` vs `pageCount`, `SizeBytes` vs `sizeBytes`).
- Nullable wire fields use `?` (`string?`, `int?`). `System.Text.Json` round-trips JSON `null` vs missing distinctly when `JsonIgnoreCondition.WhenWritingNull` is configured.
- `RenderMetadata` is `Dictionary<string, object?>` with only primitive-typed values (string, number, bool, null). Enforce at validation time inside the namespace methods, matching Node's `assertPrimitiveMetadata`.

### 4.1 Source generation (AOT-friendly)

Once the type set stabilises after Phase 2, add a `[JsonSerializable(typeof(…))]` partial `PoliPageJsonContext` class so the SDK ships AOT-friendly. The Native AOT story matters for Azure Functions (isolated worker, fast cold start) and trimmed deployments.

```csharp
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ProjectModeInput))]
[JsonSerializable(typeof(InlineModeInput))]
[JsonSerializable(typeof(DocumentDescriptor))]
// …
internal partial class PoliPageJsonContext : JsonSerializerContext { }
```

This is a Phase 7 task — don't block the v1.0 GA on it.

---

## 5. Retry orchestration

Per spec §7 and Node's `internal/http.ts`:

| Field | Value |
|---|---|
| Default `MaxRetries` | `2` (on top of the initial attempt) |
| Default `RetryDelay` | `500ms` |
| Retryable statuses | `5xx`, `429` |
| Retryable exceptions | `HttpRequestException`, `TaskCanceledException` from the network layer |
| Backoff | `RetryDelay × 2^attempt` with jitter in `[0.5, 1.5)` (uniform random) |
| Jitter | `Random.Shared.NextDouble()` — thread-safe since .NET 6 |
| `Retry-After` cap | 30s; honour both seconds and HTTP-date formats |
| Caller cancellation | Never retry. Surface `OperationCanceledException` directly. |

Implementation: a `RetryHandler : DelegatingHandler` placed at the bottom of the handler chain, so the outer chain (idempotency, error mapping) sees the final response. The handler watches for the retryable conditions and re-sends, propagating the `CancellationToken` into `Task.Delay`.

Per-call: `RequestOptions.IdempotencyKey` overrides the auto-generated UUID via a `HttpRequestMessage.Options` slot read by `IdempotencyHandler`.

---

## 6. Exception hierarchy

Per spec §8 plus the Node SDK's predicate semantics:

```csharp
public class PoliPageException : Exception
{
    public string  Code       { get; }     // PoliPageErrorCode.* constant
    public int     StatusCode { get; }     // 0 for non-HTTP failures
    public string? RequestId  { get; }
    public PoliPageException(string code, int status, string message, string? requestId = null, Exception? inner = null)
        : base(message, inner) { … }
}

public sealed class PoliPageAuthException          : PoliPageException { … } // 401 + 403
public sealed class PoliPageNotFoundException      : PoliPageException { … } // 404
public sealed class PoliPageGoneException          : PoliPageException { … } // 410
public sealed class PoliPageValidationException    : PoliPageException { … } // 400 + 422
public sealed class PoliPageRateLimitException     : PoliPageException
{
    public TimeSpan? RetryAfter { get; }    // parsed from Retry-After header
}
public sealed class PoliPagePaymentRequiredException : PoliPageException { … } // 402
public sealed class PoliPageNetworkException       : PoliPageException { … } // wraps HttpRequestException, sockets, TLS
public sealed class PoliPageDownloadException      : PoliPageException { … } // presigned S3 fetch failed
```

`ErrorMappingHandler : DelegatingHandler` consumes the non-2xx response, parses the wire JSON `{ code, message, requestId }` envelope, and throws the matching subclass. The handler is wrapped around `RetryHandler` so retries see the raw `HttpResponseMessage` while callers see the exception.

`PoliPageErrorCode` is a `static class` with `public const string Unauthorized = "UNAUTHORIZED";` constants matching every code in spec §8 — drives the `switch` on `ex.Code` for fine-grained branching.

---

## 7. Cancellation

- Every async method ends with `CancellationToken cancellationToken = default`.
- The token is threaded into `HttpClient.SendAsync(request, cancellationToken)` and `Stream.ReadAsync(buffer, cancellationToken)` everywhere.
- Per-request timeout: `PoliPageClientOptions.RequestTimeout` (default 60s) is enforced via a `CancellationTokenSource.CreateLinkedTokenSource(callerToken, timeoutToken)` — the BCL idiom. The timeout token is created per attempt so retries reset their own deadline.
- Per-call override: `RequestOptions.RequestTimeout` if set.
- Caller-cancelled → `OperationCanceledException` / `TaskCanceledException` (BCL convention) — not a `PoliPageException`. Never retried.
- Timeout-triggered → `PoliPageException` with `Code == PoliPageErrorCode.Timeout`. Eligible for retry.

This split matches the BCL convention: `OperationCanceledException` is for caller-driven cancellation, library exceptions are for library-driven failures.

---

## 8. Dependency Injection integration

The .NET ecosystem expects DI integration. The `AddPoliPage` extension is the public hand-off point. Four details matter, all of which were subtle enough that the v0 draft of this section got every one of them wrong:

1. **`DelegatingHandler`s MUST be registered as transient.** `IHttpClientFactory` pools handler chains and a handler registered as singleton gets reused across logically distinct pipelines — state on one request bleeds into the next. `AddHttpMessageHandler<T>()` requires `T` to be in the container; the framework does not auto-register.
2. **`ValidateOnStart()` is a no-op without a `.Validate(…)` chained in.** We attach real validators so a misconfigured host fails at startup, not on the first render call in production.
3. **`PoliPageClientOptions` is a `record`.** That makes the `opts with { … }` syntax legal and keeps the type immutable post-construction. Mutation happens inside the `Action<PoliPageClientOptions>` configure callback, before validation runs.
4. **The download path uses its own named client.** See §3.4 — presigned URLs MUST NOT inherit the API pipeline's auth or UA. Registering `"PoliPage.Download"` as a separate `IHttpClientFactory` entry guarantees its `DefaultRequestHeaders` stay empty.

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddPoliPage(this IServiceCollection services, Action<PoliPageClientOptions> configure)
    {
        services.AddOptions<PoliPageClientOptions>()
            .Configure(configure)
            .Validate(o => !string.IsNullOrWhiteSpace(o.ApiKey),       "PoliPage: ApiKey is required.")
            .Validate(o => o.MaxRetries >= 0,                          "PoliPage: MaxRetries must be ≥ 0.")
            .Validate(o => o.RetryDelay > TimeSpan.Zero,               "PoliPage: RetryDelay must be > 0.")
            .Validate(o => o.RequestTimeout > TimeSpan.Zero,           "PoliPage: RequestTimeout must be > 0.")
            .ValidateOnStart();

        // Handlers MUST be transient — IHttpClientFactory's handler pool requires it.
        services.AddTransient<AuthorizationHandler>();
        services.AddTransient<IdempotencyHandler>();
        services.AddTransient<RetryHandler>();
        services.AddTransient<ErrorMappingHandler>();

        services.AddHttpClient("PoliPage", (sp, http) =>
            {
                var opts = sp.GetRequiredService<IOptions<PoliPageClientOptions>>().Value;
                http.BaseAddress = opts.BaseUrl ?? new Uri("https://api.poli.page");
                // ParseAdd, not Add: Add's RFC 7231 validator rejects some otherwise-legal product
                // tokens; ParseAdd also populates the typed UserAgent collection cleanly.
                http.DefaultRequestHeaders.UserAgent.ParseAdd($"poli-page-sdk-dotnet/{VersionInfo.Version}");
                // Authorization is injected per-request by AuthorizationHandler — never a default —
                // so per-call API-key overrides are possible and the header can't accidentally
                // leak onto a deliberately shared client.
            })
            // Handler order: outermost first. AddHttpMessageHandler wraps inward,
            // so the LAST registration sits closest to the network.
            .AddHttpMessageHandler<AuthorizationHandler>()  // outermost: adds auth on the way down
            .AddHttpMessageHandler<ErrorMappingHandler>()   // unwraps non-2xx → PoliPage*Exception
            .AddHttpMessageHandler<IdempotencyHandler>()    // injects key once, survives retries
            .AddHttpMessageHandler<RetryHandler>();         // innermost: sees raw HttpResponseMessage

        // Header-less download client — see §3.4.
        services.AddHttpClient("PoliPage.Download");

        services.AddSingleton<PoliPageClient>(sp =>
        {
            var opts    = sp.GetRequiredService<IOptions<PoliPageClientOptions>>().Value;
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var apiHttp = factory.CreateClient("PoliPage");
            var dlHttp  = factory.CreateClient("PoliPage.Download");
            var log     = sp.GetRequiredService<ILogger<PoliPageClient>>();
            return new PoliPageClient(opts with
            {
                HttpClient         = apiHttp,
                DownloadHttpClient = dlHttp,
                Logger             = log,
            });
        });

        return services;
    }
}
```

The non-DI constructor `new PoliPageClient(PoliPageClientOptions)` builds both `HttpClient`s and the handler chain in-process when `Options.HttpClient` is null — same retry / idempotency / download-isolation behaviour, no `IServiceCollection` required. The manual chain registers handlers in the same outer-to-inner order so the two code paths are behaviorally identical and unit tests can exercise either without divergence.

`PoliPageClientOptions` exposes both `HttpClient` (the API pipeline) and `DownloadHttpClient` (header-less). Both default to `null`; the constructor materialises them when null and reuses them when supplied — letting hosts route both through their own `IHttpClientFactory` (e.g. for Polly composition on the API path or a custom proxy on the download path).

---

## 9. Logging

`Microsoft.Extensions.Logging.ILogger<PoliPageClient>`:

| Level | Trigger |
|---|---|
| `Debug` | One per HTTP attempt: method, path, attempt number, idempotency key tail (last 4 chars). |
| `Warning` | One per retry: status, attempt, delay, reason. |
| `Error` | One per terminal failure: code, status, requestId. |

`Authorization` and `X-API-Key` headers are scrubbed before logging. Use `LogPropertyName`s consistent with .NET observability conventions (`http.method`, `http.url`, `http.status_code`).

For OpenTelemetry: a `static readonly ActivitySource Source = new("PoliPage", VersionInfo.Version);` and one `Source.StartActivity("poli.render")` per public method. Tracing adopters register the source on their `TracerProviderBuilder`.

---

## 10. Tests

Per the engineering guide §1 and CLAUDE.md §3:

| Layer | Where | Stack |
|---|---|---|
| Unit | `tests/PoliPage.Tests/` | xUnit + FluentAssertions + WireMock.Net |
| Integration | `tests/PoliPage.IntegrationTests/` | xUnit + `[Trait("Category", "Integration")]`; gated by `POLI_PAGE_API_KEY` |
| Type-level | n/a — C# type system is exercised by compilation. |
| Sample smoke | `samples/Demo/Program.cs` exercises every public method against the real API. |

### 10.1 Unit test pattern

```csharp
[Fact]
public async Task PdfAsync_returns_bytes_on_success()
{
    using var server = WireMockServer.Start();
    server.Given(Request.Create().WithPath("/render").UsingPost())
          .RespondWith(Response.Create().WithStatusCode(200)
              .WithHeader("Content-Type", "application/pdf")
              .WithBody("%PDF-1.4 …"));

    var client = new PoliPageClient(new PoliPageClientOptions
    {
        ApiKey  = "pp_test_unit",
        BaseUrl = new Uri(server.Url!),
    });

    var pdf = await client.Render.PdfAsync(new ProjectModeInput
    {
        Project = "p", Template = "t", Version = "1.0.0", Data = new { },
    });

    pdf.Should().NotBeEmpty();
    pdf[..4].Should().Equal(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // %PDF
}
```

### 10.2 Coverage target

≥ 90% line coverage on the public surface. Roslyn's coverage collectors (`coverlet.collector` + `ReportGenerator`) feed Codecov.

---

## 11. CI workflow (`.github/workflows/ci.yml`)

Matches the engineering guide §4:

```yaml
name: CI
on:
  push:
  pull_request:
    branches: [main]

jobs:
  test:
    strategy:
      fail-fast: false
      matrix:
        os: [ubuntu-latest]
        framework: [net8.0, net10.0]
        include:
          - os: windows-latest
            framework: net10.0
          - os: macos-latest
            framework: net10.0
    runs-on: ${{ matrix.os }}
    defaults:
      run:
        shell: bash
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: |
            8.0.x
            10.0.x
      - run: dotnet restore
      - run: dotnet format --verify-no-changes
      - run: dotnet build --configuration Release --no-restore --framework ${{ matrix.framework }} -warnaserror
      - run: dotnet test  --configuration Release --no-build  --framework ${{ matrix.framework }} --filter "Category!=Integration"
      - run: dotnet pack  --configuration Release --no-build  --output ./nupkg
      - name: Install smoke
        run: ./scripts/install-smoke.sh ./nupkg
```

Separate workflows: `integration.yml` (nightly + push-to-main), `codeql.yml` (push + pull_request + weekly), `release.yml` (workflow_dispatch with environment gate).

---

## 12. Release flow

Per engineering guide §6:

1. Bump `<Version>` in `Directory.Build.props`.
2. Move `[Unreleased]` to `[X.Y.Z] - YYYY-MM-DD` in `CHANGELOG.md`. Add MIGRATION entry if MAJOR.
3. Commit `chore(release): vX.Y.Z` on `main`.
4. Run `scripts/publish.sh` locally:
   - Pre-flight: assert on `main`, working tree clean, target tag doesn't exist.
   - Verify: `dotnet format --verify-no-changes`, `dotnet test`, integration tests if `POLI_PAGE_API_KEY` is set.
   - Pack: `dotnet pack --configuration Release --output ./nupkg`.
   - Inspect: print the `.nupkg` contents and total size.
   - Confirm: prompt before `dotnet nuget push`.
   - Tag: `git tag vX.Y.Z` (don't push automatically).
5. Push the tag manually: `git push origin vX.Y.Z`.

The `release.yml` workflow (workflow_dispatch) is the signed-artifact alternative:
- Takes the version as input, refuses to run if it doesn't match `Directory.Build.props`.
- Refuses to run if the tag already exists.
- Runs the same gates as the local script.
- Publishes with `--api-key` from a deployment environment with required reviewers (so a tag push alone never publishes).
- Pushes the tag on success.

---

## 13. Build order — eight phases

| Phase | Deliverable | Goal |
|---|---|---|
| 0 | Repo scaffolding | `PoliPage.sln`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `global.json`, CI workflow that auto-skips until phase 1 fills in the manifest. |
| 1 | `PoliPageClient` constructor + `PoliPageClientOptions` | Validation, default values, internal `HttpClient` chain. RED tests for required `ApiKey` and base URL. |
| 2 | `Render.PdfAsync` happy path | `ProjectModeInput`, wire serialization, `HttpTransport`. Mock WireMock.Net server. |
| 3 | Exception hierarchy + `ErrorMappingHandler` | All non-2xx mapping. Tests for every error code in spec §8. |
| 4 | `RetryHandler` + jitter + `Retry-After` | Tests for backoff math (deterministic via `Random` injection), max attempts, never-retry 4xx. |
| 5 | `Render.PdfStreamAsync` + `Render.PreviewAsync` + `Render.DocumentAsync` | Streaming via `HttpCompletionOption.ResponseHeadersRead`, descriptor with `OwnerHttp`. |
| 6 | `Documents.*` namespace | `GetAsync`, `PreviewAsync`, `ThumbnailsAsync`, `DeleteAsync`. Including `text/html` + `X-Document-Page-Count` parsing. |
| 7 | DI integration (`AddPoliPage`) + samples + source-generated JSON | `ServiceCollectionExtensions`, ASP.NET / Worker / Azure Function / Lambda samples, `JsonSerializerContext` for AOT. |

After Phase 7 the SDK is feature-complete. Ship `1.0.0-rc.1`, soak for a week with internal users, then `1.0.0`.

---

## 14. Suggested test runner config

The friend-assembly grant flows from the assembly that **owns** the internals to the assembly that wants to **see** them — so `InternalsVisibleTo` belongs on `src/PoliPage/PoliPage.csproj`, NOT the test csproj. The v0 draft had this backwards.

`src/PoliPage/PoliPage.csproj` — add the grant alongside the package metadata:

```xml
<ItemGroup>
  <InternalsVisibleTo Include="PoliPage.Tests" />
  <!-- When the assembly is signed in a future release:
       <InternalsVisibleTo Include="PoliPage.Tests, PublicKey=…" /> -->
</ItemGroup>
```

Note the MSBuild form — it's an `<ItemGroup>` entry, not a `<PropertyGroup>` value. The historical `[assembly: InternalsVisibleTo("…")]` attribute in `AssemblyInfo.cs` also works but the MSBuild item is the modern convention because it survives `Directory.Build.props` inheritance cleanly.

`tests/PoliPage.Tests/PoliPage.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <!-- Tests aren't a public API surface — silence missing-XML-doc warnings here only. -->
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="WireMock.Net" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="coverlet.collector" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\PoliPage\PoliPage.csproj" />
  </ItemGroup>
</Project>
```

Versions pinned in `Directory.Packages.props` (see §15). The `InternalsVisibleTo` grant lets unit tests poke at `Internal/` helpers without forcing the `Backoff` math, the jitter seam, or the header builders onto the public surface.

**Note on FluentAssertions licensing.** FluentAssertions 8.0+ moved to a paid commercial license (Xceed). Pin to the last MIT-licensed line (`7.x`) in `Directory.Packages.props`, or evaluate Shouldly / `Microsoft.Testing.Platform`'s built-in assertions before phase 1 starts. The plan assumes pinned 7.x.

---

## 15. `Directory.Build.props` baseline

Two intentional differences from the v0 draft:

- **CS1591 is NOT suppressed globally.** Doing so silences "missing XML doc on public symbol" everywhere — directly contradicting CLAUDE.md §5's "XML doc comments on every public symbol" rule. CS1591 stays an error in `src/PoliPage/`; we suppress it per-csproj in `tests/` and `samples/` (see §14).
- **Modern NuGet hygiene properties are on by default**: SourceLink, embedded sources, snupkg, package validation, deterministic builds, trim/AOT readiness. These are table stakes for a v1.0 SDK in 2026 and the cost of forgetting them later (broken Step-Into in customer debuggers, no symbol packages on nuget.org, surprise breaking changes in 1.0.1) is much higher than the cost of adding them now.

```xml
<Project>
  <PropertyGroup>
    <!-- Package identity -->
    <Version>1.0.0</Version>
    <Authors>Poli Page</Authors>
    <Company>Poli Page</Company>
    <Product>Poli Page SDK for .NET</Product>
    <Copyright>Copyright (c) 2026 Poli Page</Copyright>
    <PackageLicenseExpression>MIT</PackageLicenseExpression>
    <PackageProjectUrl>https://poli.page</PackageProjectUrl>
    <RepositoryUrl>https://github.com/poli-page/sdk-csharp</RepositoryUrl>
    <RepositoryType>git</RepositoryType>
    <PackageTags>poli-page;pdf;html;template;sdk</PackageTags>
    <PackageReadmeFile>README.md</PackageReadmeFile>

    <!-- Language & nullability -->
    <Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <LangVersion>latest</LangVersion>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- Analyzer baseline — pin so net8.0 and net10.0 matrix jobs use the same wave.
         New rule waves in newer SDKs would otherwise raise warnings asymmetrically and
         TreatWarningsAsErrors would break only one leg of the matrix. -->
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <AnalysisMode>All</AnalysisMode>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>

    <!-- XML docs: CS1591 stays an error here; tests/samples csprojs suppress locally. -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>

    <!-- Deterministic + CI-friendly builds -->
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
    <Deterministic>true</Deterministic>

    <!-- NuGet hygiene: SourceLink, embedded sources, snupkg, package validation -->
    <PublishRepositoryUrl>true</PublishRepositoryUrl>
    <EmbedUntrackedSources>true</EmbedUntrackedSources>
    <DebugType>embedded</DebugType>
    <IncludeSymbols>true</IncludeSymbols>
    <SymbolPackageFormat>snupkg</SymbolPackageFormat>
    <EnablePackageValidation>true</EnablePackageValidation>

    <!-- Trim / AOT readiness (full source-gen JSON lands in Phase 8) -->
    <IsAotCompatible>true</IsAotCompatible>
    <IsTrimmable>true</IsTrimmable>
    <EnableTrimAnalyzer>true</EnableTrimAnalyzer>
    <EnableAotAnalyzer>true</EnableAotAnalyzer>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.SourceLink.GitHub" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

### 15.1 `Directory.Packages.props` — Central Package Management

All versions pinned in one place. Verify each against the current registry before phase 0 — these are the versions consistent with .NET 10 LTS / mid-2026.

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <!-- Runtime deps -->
    <PackageVersion Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Options"              Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.Http"                 Version="10.0.0" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection"  Version="10.0.0" />
    <PackageVersion Include="System.Diagnostics.DiagnosticSource"       Version="10.0.0" />

    <!-- Build / SourceLink -->
    <PackageVersion Include="Microsoft.SourceLink.GitHub"               Version="8.0.0" />

    <!-- Analyzers -->
    <PackageVersion Include="Microsoft.CodeAnalysis.NetAnalyzers"       Version="9.0.0" />
    <PackageVersion Include="Meziantou.Analyzer"                        Version="2.0.196" />
    <PackageVersion Include="Roslynator.Analyzers"                      Version="4.13.1" />

    <!-- Test deps. FluentAssertions pinned to the last MIT-licensed line — 8.x is commercial. -->
    <PackageVersion Include="xunit"                                     Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio"                 Version="2.8.2" />
    <PackageVersion Include="FluentAssertions"                          Version="7.0.0" />
    <PackageVersion Include="WireMock.Net"                              Version="1.6.7" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk"                    Version="17.11.1" />
    <PackageVersion Include="coverlet.collector"                        Version="6.0.2" />
  </ItemGroup>
</Project>
```

Analyzer packages reference themselves in `src/PoliPage/PoliPage.csproj` as `<PackageReference Include="…" PrivateAssets="all" />` so they participate in the build but don't flow as transitive dependencies to consumers.

---

## 16. Observability hooks

Per spec §9.5 and Node's `OnRetry` / `OnError` shape:

```csharp
public sealed record RetryEvent(int Attempt, TimeSpan Delay, HttpStatusCode? StatusCode, string Reason);

new PoliPageClientOptions
{
    OnRetry = evt   => metrics.Counter("poli.retry").Add(1, ("attempt", evt.Attempt)),
    OnError = error => sentry.CaptureException(error),
};
```

Hooks are synchronous (callable from any context, including `Task.Run` continuations). The retry hook fires **before** `Task.Delay`. The error hook fires **before** the exception is thrown to the caller. Both are wrapped in `try { … } catch { /* swallow + log */ }` so a faulty hook never breaks the request.

---

## 17. Differences from sdk-go

Most of the architecture is shared with the Go SDK (statically-typed sync client, transport seam, pure-function internals). The deltas:

| Concern | Go | .NET |
|---|---|---|
| Concurrency model | goroutines + `context.Context` | `Task<T>` + `CancellationToken` |
| Options idiom | functional options (`option.WithX`) | options object (`PoliPageClientOptions`) |
| HTTP middleware | inject `http.RoundTripper` | `DelegatingHandler` chain |
| Error model | single `*polipage.Error` + `errors.Is` sentinels | exception hierarchy with specific subclasses (BCL idiom) |
| DI integration | n/a (Go doesn't have it) | `services.AddPoliPage(...)` extension |
| Logging | `log/slog` | `Microsoft.Extensions.Logging.ILogger<T>` |
| Wire serialization | `encoding/json` field tags | `System.Text.Json` + `[JsonPropertyName]` |
| File helper | `polipage.RenderToFile(ctx, client, input, path)` free function | `PoliPageClient.RenderToFileAsync(input, path, …)` static method |
| Release artifact | tag push (Go modules) | `.nupkg` push to NuGet |
| AOT story | trivial (Go is AOT) | source-generated `JsonSerializerContext` in phase 7 |

The Node SDK remains the canonical reference for ambiguous wire-level behaviour.

---

## 18. Open questions

These need decisions before the relevant phase starts; track them in the issue tracker once the repo is wired up.

1. **`netstandard2.0` target?** Including it doubles compatibility (legacy .NET Framework apps) but adds ~30 hours of conditional code (no `Random.Shared`, no `init`-only properties, no `record` types). Recommendation: defer — start with `net8.0` + `net10.0` only, revisit if a paying customer asks. Track in the engineering guide §10 open-questions section.
2. **Source-generated JSON vs reflection-based?** Source generation is AOT-friendly and faster but constrains how `RenderMetadata` (dynamic `Dictionary<string, object?>`) is serialized. Recommendation: ship reflection-based for v1.0, add source generation as a v1.1 perf release once the AOT story is exercised.
3. **`IAsyncDisposable` on `PoliPageClient`?** The internal `HttpClient` should be disposed when the SDK owns it. If the caller passed in their own, we shouldn't dispose it. Recommendation: implement `IDisposable` + `IAsyncDisposable`, dispose only if `Options.HttpClient` was null on construction.
4. **OpenTelemetry semantic conventions?** Use the official `OpenTelemetry.Semantic.Conventions` attribute names (`http.request.method`, etc.) once they stabilise. They are in preview as of 2026-05 — pin to whatever is current at v1.0, document the version.
5. **Native AOT publishing for the demo sample?** The `samples/Demo/Program.cs` is the cross-language smoke test — making it `PublishAot=true` proves the SDK is AOT-clean. Defer to v1.1 alongside source-generated JSON.

When in doubt, the rule from `sdk-specification.md` §10 applies: open a discussion in `poli-page/sdk-node` or with Xavier directly. Public-API decisions ripple across the fleet; don't make them solo.
