# Migration Guide

This file documents breaking changes between major versions of the
`PoliPage` NuGet package. We follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html):
breaking changes only ship in major version bumps and always come with
an entry here.

## 1.0

The first stable release. Treat `1.0.0` as the starting point for the
.NET SDK — there is no prior published surface to migrate from.

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

`1.0.0` of the .NET SDK is behaviour-identical to `@poli-page/sdk@1.0`:
same retry policy (5xx + 429 + network + timeout; jitter `[0.5, 1.5)`;
`Retry-After` cap 30s), same error-code round-tripping, same predicate
exceptions (`PoliPageAuthException` covers 401 + 403), same constructor
validation, same hooks-never-break-the-request semantics, same
project-mode-only constraint on `Render.PdfAsync` /
`PdfStreamAsync` / `DocumentAsync`, same primitive-only `Metadata`,
same thumbnails wire wrap/unwrap, same `Documents.PreviewAsync`
`text/html` + `X-Document-Page-Count` parsing.
