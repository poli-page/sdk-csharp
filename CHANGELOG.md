# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [0.10.0] - 2026-10-26

Breaking: implements the environments contract. A key carries its
environment (`pp_{env}_…`, e.g. `pp_sandbox_…`, `pp_live_…`,
`pp_staging_…`) and a render uses the project's copy published in that
environment. There is no deprecation window and no compatibility shim.
See [MIGRATION.md](MIGRATION.md#from-09x-to-0100) for the upgrade guide.

### Removed

- **BREAKING**: `Render.PreviewAsync` and `PreviewResult`: the API removed
  `POST /v1/render/preview`. `Documents.PreviewAsync(id)` (the stored HTML of
  a rendered document) is unchanged.
- **BREAKING**: `InlineModeInput` (inline-HTML rendering) and the abstract `RenderInput`
  base record.
- **BREAKING**: `ProjectModeInput.Version` and `DocumentDescriptor.Version`: there are no
  versions to pick.
- **BREAKING**: `PoliPagePaymentRequiredException`: no route the SDK calls answers `402`;
  payment grace is `403 ORGANIZATION_PAYMENT_GRACE`.
- **BREAKING**: `PoliPageErrorCode` constants `VersionNotFound`, `InvalidVersionFormat`,
  `VersionRequired`, `InvalidVersionForKeyEnv`, `MissingProjectOrTemplate`,
  `Gone` (the API answers `410 DOCUMENT_GONE`), `PaymentRequired` and
  `StorageRequired`.

### Changed

- **BREAKING**: `ProjectModeInput` is renamed `RenderInput` and is the only render input.
  It is a sealed record with seven body fields: `Project`, `Template`,
  `Data` (required) and `Metadata`, `Format`, `Orientation`, `Locale`
  (optional). `Data` is now `required`. Unset optional fields are omitted
  from the body, never sent as `null`.
- **BREAKING**: API errors: the code is read from the body's `code` field, else its `error`
  field, else `unknown_error`; it is never guessed from the HTTP status. A
  body that is not JSON gives `INTERNAL_ERROR` with the real status. The
  message is `detail`, else `title`, else `message`, else `HTTP <status>`
  (the HTTP reason phrase is no longer used). Unknown codes pass through
  verbatim.
- `DocumentDescriptor.Environment` is documented as any environment name
  (e.g. `sandbox`, `live`, `staging`) and is returned unchanged.
- **BREAKING**: `ThumbnailOptions.Width` and `ThumbnailOptions.Format` are nullable with no
  SDK default (they used to send `200` and `png`). An unset option is not
  sent. `Documents.ThumbnailsAsync(id)` takes the options as an optional
  parameter; with none set the body is `{}` and the API defaults apply
  (width 400, `jpeg`). The SDK does not check option bounds; the API answers
  `400 VALIDATION_ERROR`.
- `Documents.DeleteAsync` is documented as idempotent: the API answers `204`
  for an already-deleted document too.
- **BREAKING**: The default code of `PoliPageGoneException` is `DOCUMENT_GONE`.
- README, docs site and `samples/Demo` follow the environments contract:
  the quick start renders `getting-started`/`welcome` with a `sandbox` key
  and no version, the "Authentication & environments" section carries the
  key table and the full-key regex, and previews and inline HTML point to
  the playground (https://play.poli.page).
- The demo asks for a `sandbox` key, checks a pasted key against the
  full-key regex only, and ends with the `not-found` step (a render of a
  template that does not exist, `404 TEMPLATE_NOT_FOUND`), printing the
  error's code, status, message and request id.
- The docs site's error-code table is generated from `PoliPageErrorCode`,
  so it no longer lists retired codes.

### Added

- `ThumbnailFormat.Webp`.
- `PoliPageErrorCode` constants for the API codes `PublishInProgress`,
  `EnvironmentMismatch`, `ProjectNotInEnvironment`, `TemplateNotFound`,
  `DocumentGone`, `EnvironmentProtected`, `OrganizationPaymentGrace`,
  `EmptyDocument`, `StorageAccessDenied`, `OrganizationMigrating`,
  `ProjectNotFound`, `TemplateError`, `ThumbnailsNotAvailable`,
  `RateLimitExceeded`, `RenderBodyTooLarge`, `DocumentTooLarge`,
  `RenderTimeout`, and `InternalError`, which the API already returned.
- `503 PUBLISH_IN_PROGRESS` (a render waiting on a publish) is retried like
  any other 5xx and honours `Retry-After`; no new option is needed.
- Integration tests (`tests/PoliPage.IntegrationTests`, trait
  `Category=Integration`) render `getting-started`/`welcome` with a
  `sandbox` key, then get, preview and delete the stored document. The new
  `Integration` workflow runs them nightly and on demand when the
  `POLI_PAGE_API_KEY` secret is set, and logs a skip otherwise. The tests
  skip on a key that is not `pp_sandbox_…`, and the workflow fails on one.

## [0.9.1] - 2026-06-22

### Fixed

- Wrap transport failures (DNS / connection / TLS) on the presigned-download
  path (`DownloadAsync` / `DownloadStreamAsync`) in a typed
  `PoliPageDownloadException` (code `download_failed`) instead of leaking a raw
  `HttpRequestException`. Caller cancellation is still propagated unchanged.

## [0.9.0] - 2026-06-20

First public release of the Poli Page .NET SDK. Targets `net8.0` and
`net10.0`. Behaviour parity with `@poli-page/sdk@1.0` (see
[MIGRATION.md](MIGRATION.md#090) for the parity checklist).

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

[Unreleased]: https://github.com/poli-page/sdk-csharp/compare/v0.10.0...HEAD
[0.10.0]: https://github.com/poli-page/sdk-csharp/compare/v0.9.1...v0.10.0
[0.9.1]: https://github.com/poli-page/sdk-csharp/compare/v0.9.0...v0.9.1
[0.9.0]: https://github.com/poli-page/sdk-csharp/releases/tag/v0.9.0
