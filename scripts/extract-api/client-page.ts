import type { XmlDocMember } from './xml-docs.js';
import type { PublicType } from './registry.js';

export function buildClientPage(
  client: PublicType,
  xml: ReadonlyMap<string, XmlDocMember>,
): string {
  const doc = xml.get(client.memberId);
  const lede =
    doc?.summary ||
    'The PoliPage client — the single entry point to the .NET SDK.';

  return `---
title: Client
description: The PoliPageClient class — the only entry point to the .NET SDK.
---

import MethodSignature from '@preset/components/MethodSignature.astro';

<MethodSignature lang="csharp" code={\`public sealed class PoliPageClient : IDisposable\`} />

${lede}

## Constructor

The constructor takes a single \`PoliPageClientOptions\` record. The only required field is \`ApiKey\`. See [\`PoliPageClientOptions\`](../types/) for every field.

\`\`\`csharp
var client = new PoliPageClient(new PoliPageClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("POLI_PAGE_API_KEY")!,
});
\`\`\`

## Namespaces

The client exposes the operations through a namespaced property:

- [\`Render\`](./methods/render-pdf/) — render PDFs (bytes, streaming, preview, or stored document).

## See also
- [Types](../types/)
- [Errors](../errors/)
- [Runtime support](../runtime-support/)
`;
}
