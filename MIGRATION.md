# Migration Guide

This file documents breaking changes between releases of the `PoliPage`
NuGet package. We follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html): while the
package is on 0.x, a breaking change bumps the minor version; from 1.0 on,
only a major version bump breaks. Every breaking release has an entry here.

## From 0.10.0 to 0.11.0

The SDK now follows the Poli Page API contract
([poli-page/api-contract](https://github.com/poli-page/api-contract)): the
retry rules, the SDK's error messages and caller cancellation are the same in
every Poli Page SDK. See the [CHANGELOG](CHANGELOG.md#0110---2026-10-30) for the
full list.

### Cancellation throws `PoliPageException` with code `aborted`

Cancelling a call's `CancellationToken` no longer lets
`OperationCanceledException` propagate: the SDK throws a `PoliPageException`
with `Code == PoliPageErrorCode.Aborted`, the `OperationCanceledException`
as `InnerException`. `OnError` receives it too.

```csharp
// 0.10.0
catch (OperationCanceledException) when (cts.Token.IsCancellationRequested) { /* cancelled */ }

// 0.11.0
catch (PoliPageException ex) when (ex.Code == PoliPageErrorCode.Aborted) { /* cancelled */ }
```

The same holds while a PDF is read: a cancelled read of the
`Render.PdfStreamAsync` stream throws `aborted`.

### Quota and storage-access errors are not retried

`QUOTA_EXCEEDED`, `OVERAGE_CAP_EXCEEDED` and `STORAGE_ACCESS_DENIED` surface
at once, and `IsRetryable()` is `false` for them. Code that requeues a job on
`IsRetryable()` stops requeueing these: wait for the monthly reset, raise the
overage cap, or restore the bucket's access. `IsRetryable()` is also `false`
for every `DOWNLOAD_FAILED`; call `Documents.GetAsync(id)` for a fresh
presigned URL and download again.

### `RetryEvent.Attempt` counts from the first attempt

`RetryEvent.Attempt` is the attempt about to start: `2` for the first retry,
`3` for the second, as `RequestEvent.Attempt` numbers them. Subtract 1 for
the retry number.

### Download failures are all `DOWNLOAD_FAILED`

A timeout of the presigned download (`DownloadHttpClient.Timeout`, which bounds
the wait for the storage's response headers) throws
`PoliPageDownloadException` (`DOWNLOAD_FAILED`, no status) instead of
`TaskCanceledException`; a `catch (TaskCanceledException ex) when
(ex.InnerException is TimeoutException)` around a render or a download no
longer matches.

### Messages

Code that matched the SDK's own messages must use the new texts:
`Network request failed`, `Request timed out after <ms>ms`,
`Request was aborted`, `apiKey is required`,
`Failed to download PDF: HTTP <status>` and
`Failed to download PDF: network error`. Prefer comparing `Code`.

### Empty document ids reach the API

`Documents.*` methods no longer throw `ArgumentException` for an empty or
whitespace id: the API answers it (`404 NOT_FOUND` for an empty id). Only a
`null` id throws (`ArgumentNullException`).

## From 0.9.x to 0.10.0

0.10.0 implements the environments contract. An API key belongs to exactly
one environment of your organization, and its text names it: a key is
`pp_{env}_{32 lowercase hex}` (`pp_sandbox_…`, `pp_live_…`,
`pp_staging_…`). A render uses the project's copy published in the key's
environment. Every removal ships at once, with no deprecation window and no
compatibility shim.

### API keys

The old test and service-account key prefixes are gone: standard and
service keys share the `pp_{env}_…` format. Create a key in the environment
you render in; a `sandbox` key (watermarked, not counted toward your quota,
documents expire after 30 days) replaces a test key. The SDK does not check
the key's format, and it never sends an environment.

### Render input: `ProjectModeInput` → `RenderInput`, no `Version`

`ProjectModeInput` is renamed `RenderInput` and is the only render input.
`Version` is removed: publish the project to the key's environment instead
of pinning a version. `Data` is now `required`.

```csharp
// 0.9.x
byte[] pdf = await client.Render.PdfAsync(new ProjectModeInput
{
    Project  = "billing",
    Template = "invoice",
    Version  = "1.0.0",
    Data     = new { invoiceNumber = "INV-001" },
});

// 0.10.0
byte[] pdf = await client.Render.PdfAsync(new RenderInput
{
    Project  = "billing",
    Template = "invoice",
    Data     = new { invoiceNumber = "INV-001" },
});
```

`DocumentDescriptor.Version` is removed too. `DocumentDescriptor.Environment`
carries any environment name (`sandbox`, `live`, `staging`, …).

### Preview and inline HTML

`Render.PreviewAsync`, `PreviewResult` and `InlineModeInput` are removed,
because the API removed `POST /v1/render/preview`. Preview templates and
render inline HTML in the playground, <https://play.poli.page>.
`Documents.PreviewAsync(id)` (the stored HTML of a rendered document) stays.

### Errors

- The code comes from the API's error body (`code`, else `error`), never
  from the HTTP status; a body with neither gives `unknown_error`, and a body
  that is not JSON gives `INTERNAL_ERROR`. Unknown codes pass through
  verbatim.
- `PoliPagePaymentRequiredException` is removed. Payment grace is
  `403 ORGANIZATION_PAYMENT_GRACE`, raised as `PoliPageAuthException`.
- Removed constants: `VersionNotFound`, `InvalidVersionFormat`,
  `VersionRequired`, `InvalidVersionForKeyEnv`, `MissingProjectOrTemplate`,
  `Gone` (use `DocumentGone`), `PaymentRequired` (use
  `OrganizationPaymentGrace`) and `StorageRequired`.
- New constants include `TemplateNotFound`, `ProjectNotFound`,
  `ProjectNotInEnvironment`, `DocumentGone`, `PublishInProgress` and
  `InternalError`; see the [CHANGELOG](CHANGELOG.md) for the full
  list.

### Thumbnails

`ThumbnailOptions.Width` and `ThumbnailOptions.Format` are nullable with no
SDK default (they used to send `200` and `png`). Unset, the API defaults
apply. The SDK no longer checks option bounds; the API answers
`400 VALIDATION_ERROR`.

## 0.9.0

The first release. Treat `0.9.0` as the starting point for the .NET
SDK — there is no prior published surface to migrate from.

### Surface

```csharp
using PoliPage;

var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
});

// Render namespace
//   client.Render.PdfAsync, PdfStreamAsync, DocumentAsync → project mode only
//                                                          (Project + Template + Version)
//   client.Render.PreviewAsync                            → both project and inline mode

// Documents namespace (stored-document lifecycle)
//   client.Documents.GetAsync, PreviewAsync, ThumbnailsAsync, DeleteAsync

// DI extension
//   services.AddPoliPage(options => { … });

// File helper (static method)
//   PoliPageClient.RenderToFileAsync(input, path, cancellationToken: ct)
```

### Behaviour parity with `@poli-page/sdk@1.0`

`0.9.0` of the .NET SDK is behaviour-identical to `@poli-page/sdk@1.0`:
same retry policy (5xx + 429 + network + timeout; jitter `[0.5, 1.5)`;
`Retry-After` cap 30s), same error-code round-tripping, same predicate
exceptions (`PoliPageAuthException` covers 401 + 403), same constructor
validation, same hooks-never-break-the-request semantics, same
project-mode-only constraint on `Render.PdfAsync` /
`PdfStreamAsync` / `DocumentAsync`, same primitive-only `Metadata`,
same thumbnails wire wrap/unwrap, same `Documents.PreviewAsync`
`text/html` + `X-Document-Page-Count` parsing.
