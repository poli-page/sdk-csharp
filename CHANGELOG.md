# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.9.0] - 2026-06-20

First public release of the Poli Page .NET SDK. Targets `net8.0` and
`net10.0`. Behaviour parity with `@poli-page/sdk@1.0` (see
[MIGRATION.md](MIGRATION.md#10) for the parity checklist).

### Added

- `PoliPageClient` constructed via `new PoliPageClient(PoliPageClientOptions)`.
- `PoliPageClientOptions` with `ApiKey`, `BaseUrl`, `MaxRetries`,
  `RetryDelay`, `RequestTimeout`, `HttpClient`, `DownloadHttpClient`,
  `Logger`, `OnRetry`, `OnError`.
- Per-call `RequestOptions` with `IdempotencyKey`, `RequestTimeout`,
  `Headers`.
- `services.AddPoliPage(...)` extension for DI; registers `PoliPageClient`
  as a singleton with a named `IHttpClientFactory`-provided `HttpClient`.
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
- `Thumbnail` exposes the canonical wire fields used by every other
  Poli Page SDK (Node, Python, Ruby, Rust, Go, Java, PHP): `Page`
  (`page`), `ContentType` (`contentType`, a MIME type such as
  `image/png`), and `Data` (`data`).
- Runnable sample at `samples/Demo/` exercising every public method
  against the real API. First-run prompts for a `pp_test_*` key and
  persists to `.env`; subsequent runs are silent.

### Build & supply chain

- Targets: `net8.0` (LTS) and `net10.0` (LTS). `netstandard2.0` is an
  open question tracked in `sdk-csharp-plan.md` §18.
- Zero non-Microsoft runtime dependencies — all runtime references are
  Microsoft / BCL-adjacent: `Microsoft.Extensions.Logging.Abstractions`,
  `Microsoft.Extensions.Options`, `Microsoft.Extensions.Http`,
  `Microsoft.Extensions.DependencyInjection.Abstractions`, and
  `System.Diagnostics.DiagnosticSource`.
- `<Nullable>enable</Nullable>` + `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`.
- `.editorconfig` enforced; `dotnet format --verify-no-changes` in CI.
- Roslyn analyzers: `Microsoft.CodeAnalysis.NetAnalyzers`,
  `Meziantou.Analyzer`, `Roslynator.Analyzers`.
- Deterministic build, embedded symbols (PDBs inside the assembly), and
  Source Link via `Microsoft.SourceLink.GitHub`.

[Unreleased]: https://github.com/poli-page/sdk-csharp/compare/v0.9.0...HEAD
[0.9.0]: https://github.com/poli-page/sdk-csharp/releases/tag/v0.9.0
