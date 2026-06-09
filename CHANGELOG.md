# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Initial scaffolding for the Poli Page .NET SDK. Targets `net8.0` and
`net10.0`. Behaviour parity with `@poli-page/sdk@1.0` (see
[MIGRATION.md](MIGRATION.md#10) for the parity checklist) is the goal
for the first `1.0.0` release.

### Breaking changes

- `Thumbnail.PageNumber` renamed to `Thumbnail.Page` to match the
  canonical wire format used by every other Poli Page SDK (Node,
  Python, Ruby, Rust, Go, Java, PHP). The wire field is `page`, not
  `pageNumber`.
- `Thumbnail.Format` renamed to `Thumbnail.ContentType` and is now a
  MIME type (e.g., `"image/png"`, `"image/jpeg"`) instead of the
  short format name (`"png"`, `"jpeg"`). Matches the canonical wire
  field `contentType`.
- `Thumbnail.Base64Data` renamed to `Thumbnail.Data` to match the
  canonical wire field `data`.

### Planned

- `PoliPageClient` constructed via `new PoliPageClient(PoliPageClientOptions)`.
- `PoliPageClientOptions` with `ApiKey`, `BaseUrl`, `MaxRetries`,
  `RetryDelay`, `RequestTimeout`, `HttpClient`, `Logger`, `OnRetry`,
  `OnError`.
- Per-call `RequestOptions` with `IdempotencyKey`, `RequestTimeout`,
  `Headers`.
- `services.AddPoliPage(...)` extension for DI; registers as singleton
  with a named `IHttpClientFactory`-provided `HttpClient`.
- Render namespace: `Render.PdfAsync`, `Render.PdfStreamAsync`,
  `Render.PreviewAsync`, `Render.DocumentAsync`.
- Documents namespace: `Documents.GetAsync`, `Documents.PreviewAsync`,
  `Documents.ThumbnailsAsync`, `Documents.DeleteAsync`.
- `DocumentDescriptor.DownloadPdfAsync(CancellationToken)` using the
  parent client's `HttpClient` (no auth, no retry).
- `PoliPageClient.RenderToFileAsync(input, path, …)` — streams the PDF
  to disk via `Render.PdfStreamAsync` + `Stream.CopyToAsync`.
- Sealed `RenderInput` base type satisfied by `ProjectModeInput` and
  `InlineModeInput` only — `PdfAsync` / `PdfStreamAsync` /
  `DocumentAsync` enforce project-mode-only at compile time.
- Exception hierarchy rooted at `PoliPageException` with subclasses
  `PoliPageAuthException`, `PoliPageNotFoundException`,
  `PoliPageGoneException`, `PoliPageValidationException`,
  `PoliPageRateLimitException`, `PoliPagePaymentRequiredException`,
  `PoliPageNetworkException`, `PoliPageDownloadException`. Each carries
  `Code`, `StatusCode`, `Message`, `RequestId`.
- `PoliPageErrorCode` static class with code constants matching the
  spec (`Unauthorized`, `Forbidden`, `NotFound`, `VersionNotFound`,
  `DocumentNotFound`, `Gone`, `Validation`, `RateLimit`, `Timeout`,
  `Network`, `DownloadFailed`, `PaymentRequired`,
  `OrganizationCancelled`, `OrganizationPurged`, `QuotaExceeded`,
  `OverageCapExceeded`, `InvalidVersionFormat`, `VersionRequired`,
  `InvalidVersionForKeyEnv`, `StorageRequired`).
- Retry loop: exponential backoff with jitter `[0.5, 1.5)`,
  `Retry-After` honoured up to 30s, cancellable mid-flight via
  `CancellationToken`.
- `Microsoft.Extensions.Logging` integration via
  `PoliPageClientOptions.Logger` — DEBUG/attempt, WARN/retry,
  ERROR/terminal. `Authorization` header never logged.
- OpenTelemetry `ActivitySource` named `PoliPage` for distributed
  tracing.
- Runnable sample at `samples/Demo/` exercising every public method
  against the real API. First-run prompts for `pp_test_*` key and
  persists to `.env`; subsequent runs are silent.

### Build & supply chain

- Targets: `net8.0` (LTS) and `net10.0` (LTS). `netstandard2.0` is an
  open question tracked in `sdk-csharp-plan.md` §18.
- Zero non-Microsoft runtime dependencies for the core package
  (`Microsoft.Extensions.Logging.Abstractions`,
  `System.Diagnostics.DiagnosticSource` only).
- `<Nullable>enable</Nullable>` + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- `.editorconfig` enforced; `dotnet format --verify-no-changes` in CI.
- Roslyn analyzers: `Microsoft.CodeAnalysis.NetAnalyzers`,
  `Meziantou.Analyzer`, `Roslynator.Analyzers`.
