# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

Conformance with the Poli Page API contract v1.3.0
([poli-page/api-contract](https://github.com/poli-page/api-contract)). See
[MIGRATION.md](MIGRATION.md#from-0100-to-the-next-release-unreleased) for
the upgrade guide.

### Changed

- **BREAKING**: `QUOTA_EXCEEDED` and `OVERAGE_CAP_EXCEEDED` (429) and
  `STORAGE_ACCESS_DENIED` (502) are no longer retried, even with a
  `Retry-After` header, and `IsRetryable()` now returns `false` for them (it
  returned `true` for any 429 or 5xx). They last until the monthly reset or
  until the bucket access is restored, so the SDK used to wait up to 30 s per
  retry for nothing. `RATE_LIMIT_EXCEEDED` and every other 429 and 5xx are
  still retried.
- **BREAKING**: `IsRetryable()` is `false` for every `DOWNLOAD_FAILED`,
  whatever the storage status: it was `true` for a storage 5xx or 429. The
  SDK never retried the download itself, and still does not.
- **BREAKING**: cancelling the `CancellationToken` of a call throws
  `PoliPageException` with `Code == PoliPageErrorCode.Aborted` (message
  `Request was aborted`, the `OperationCanceledException` as
  `InnerException`) instead of letting `OperationCanceledException`
  propagate: during an attempt, during the wait before a retry (at once), and
  while a PDF is read, from `Render.PdfAsync`, `DocumentDescriptor.DownloadPdfAsync`,
  `RenderToFileAsync` or the stream of `Render.PdfStreamAsync` (a `CopyToAsync`
  of that stream cancelled while its destination writes included). `aborted` is never retried and
  `OnError` now receives it. `catch (OperationCanceledException)` around an
  SDK call no longer catches a cancellation; catch `PoliPageException` with
  `Code == PoliPageErrorCode.Aborted`.
- **BREAKING**: `RetryEvent.Attempt` is the number of the attempt about to
  start, as `RequestEvent.Attempt` numbers it: `2` for the first retry (it was
  `1`).
- **BREAKING**: `Documents.GetAsync`, `PreviewAsync`, `ThumbnailsAsync` and
  `DeleteAsync` send an empty or whitespace id to the API, which answers it
  (an empty id reaches no route: `404 NOT_FOUND`), instead of throwing
  `ArgumentException`. A `null` id still throws `ArgumentNullException`.
- **BREAKING**: a timeout of the presigned-URL download (the
  `DownloadHttpClient`'s `Timeout`, which bounds the wait for the storage's
  response headers; the PDF body read has no SDK time bound) throws `PoliPageDownloadException`
  (`DOWNLOAD_FAILED`, `Failed to download PDF: network error`, no status, the
  `TaskCanceledException` as `InnerException`) instead of a raw
  `TaskCanceledException`. Every download failure is now `DOWNLOAD_FAILED`.
- SDK error messages are the exact texts of the SDK specification:
  - `network_error`: `Network request failed` (was `Network error during request.`);
    the `HttpRequestException` is still the `InnerException`.
  - `timeout`: `Request timed out after <ms>ms`, the attempt's timeout in
    milliseconds (was `Request timed out.`). When an injected `HttpClient`'s
    own, shorter `Timeout` fires first, the message names that bound.
  - `invalid_options` for a missing key: `apiKey is required` (was
    `PoliPage: ApiKey is required.`).
  - `DOWNLOAD_FAILED`: `Failed to download PDF: HTTP <status>` (was
    `Presigned download failed: HTTP <status>`), and
    `Failed to download PDF: network error` when no complete answer came
    back (was `Presigned download failed: network error.`).
- `Retry-After` is read as integer seconds or an HTTP-date only, clamped to
  0–30 s. An integer too large for an `int` now clamps to 30 s (it was
  ignored, and the backoff applied). `PoliPageRateLimitException.RetryAfter`
  follows the same rule. `Retry-After-Ms` is ignored, as before.
- An API answer's whole body is read inside its attempt, under
  `RequestTimeout`. A 2xx answer cut mid-body is a `network_error` and one
  that stalls a `timeout`, both retried; an error answer whose body fails while
  it is read is `INTERNAL_ERROR` with the message `HTTP <status>`, its real
  status and `X-Request-Id`, the read error as `InnerException`, and is
  retried by its status (a 503 yes, a 400 no). A render whose 2xx answer is cut
  was already stored by the API, so its retry can store a second document.
- `OnResponse` fires once the 2xx body is read, and its `DurationMs` covers
  the body; it never fires for a 2xx whose body failed.
- `RequestTimeout` bounds each attempt, connection, headers and body
  together, whatever `HttpClient` you inject, even one whose handler ignores
  cancellation. The `HttpClient` the SDK creates for API calls, and the
  `"PoliPage"` client `AddPoliPage` registers, no longer have
  `HttpClient.Timeout`'s 100 s default, which cut a longer `RequestTimeout`
  short.
- The request id of an API error is the `X-Request-Id` header, as the SDK
  specification defines it; the body's `requestId` is the fallback.

### Fixed

- A GET or DELETE (`Documents.GetAsync`, `PreviewAsync`, `DeleteAsync`) and
  the presigned download are never sent again behind the SDK's back.
  SocketsHttpHandler resent a request without content up to three more times
  when its connection closed before any answer, on a reused keep-alive
  connection or a fresh one: those sends were not counted by `MaxRetries` nor
  reported to `OnRequest`, and the download, which the SDK never retries, was
  retried. Every SDK request now carries content (an empty body,
  `Content-Length: 0`, on GET and DELETE), so the failure surfaces as
  `network_error` and goes through the SDK's own retry, or as
  `DOWNLOAD_FAILED`. A POST (`Render.*`, `Documents.ThumbnailsAsync`) was
  never resent. Every request is also pinned to HTTP/1.1, even through an
  injected `HttpClient` whose `DefaultRequestVersion` asks for HTTP/2, where
  the handler resends a refused or unprocessed stream by itself.
- A failure while the PDF body is read no longer escapes as the HTTP stack's
  exception: `Render.PdfAsync`, `DocumentDescriptor.DownloadPdfAsync` and
  reads of the `Render.PdfStreamAsync` stream throw `DOWNLOAD_FAILED`
  (`Failed to download PDF: network error`, the read error as
  `InnerException`), or `aborted` on cancellation.
- A retry wait is never shorter than its delay: when the timer fires early,
  the SDK waits again for the rest, on the monotonic clock.
- Docs: the retries page no longer claims the API deduplicates on
  `Idempotency-Key`. It does not: the key is stable across the retries of one
  call, and a render retried after a late 5xx, a `timeout` or a
  `network_error` can store a second document. The `DownloadHttpClient` docs
  no longer say the API client is used when it is unset.

### Added

- Known error code `PoliPageErrorCode.TemplateUnsafe` (`TEMPLATE_UNSAFE`,
  400: the template, a partial or an SVG asset holds an injection attempt;
  the message names it; not retried), next to `TemplateError`.
- `tests/PoliPage.Tests/Contract` runs every SDK case of the API contract
  against a local mock server, with the SDK's own `HttpClient` and through an
  injected one. CI pins the contract by commit (`poli-page/api-contract`
  action) and Dependabot proposes each new version. Run it locally with
  `POLI_CONTRACT=<path to contract.json> dotnet test`.

## [0.10.0] - 2026-10-27

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
