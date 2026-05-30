import type { PublicType } from './registry.js';
import type { XmlDocMember } from './xml-docs.js';

export function buildTypesPage(
  types: readonly PublicType[],
  xml: ReadonlyMap<string, XmlDocMember>,
): string {
  const blocks: string[] = [];
  for (const t of types) {
    const doc = xml.get(t.memberId);
    const summary = doc?.summary || `_(See the source for the full definition.)_`;
    blocks.push(`### \`${t.name}\` (${t.kind})\n\n${summary}\n`);
  }

  return `---
title: Types
description: Public types exported from the PoliPage NuGet package.
---

The .NET SDK exposes the types below. They live under the \`PoliPage\` namespace:

\`\`\`csharp
using PoliPage;
\`\`\`

${blocks.join('\n')}

For the full set of fields and XML doc comments on each type, see [the source on GitHub](https://github.com/poli-page/sdk-csharp/tree/main/src/PoliPage).
`;
}
