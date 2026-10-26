import type { PublicException } from './registry.js';
import type { XmlDocMember } from './xml-docs.js';

export interface ErrorCodeConstant {
  readonly name: string;
  readonly value: string;
}

// Reads the `public const string Name = "VALUE";` lines of PoliPageErrorCode.cs,
// so the docs table cannot drift from the constants the SDK ships.
export function readErrorCodes(source: string): ErrorCodeConstant[] {
  const codes = [...source.matchAll(/public const string (\w+) = "([^"]+)";/g)].map(
    (m) => ({ name: m[1]!, value: m[2]! }),
  );
  if (codes.length === 0) {
    throw new Error('extractor: no constants found in PoliPageErrorCode.cs');
  }
  return codes;
}

export function buildErrorsPage(
  exceptions: readonly PublicException[],
  xml: ReadonlyMap<string, XmlDocMember>,
  codes: readonly ErrorCodeConstant[],
): string {
  const rows = exceptions.map((e) => {
    const doc = xml.get(e.memberId);
    return {
      code: e.name,
      when: doc?.summary || `${e.name} — see source for details.`,
      recovery: `HTTP ${e.statusCode}. Codes: ${e.codes.join(', ')}.`,
    };
  });

  const codeRows = codes.map((c) => `| \`${c.name}\` | \`${c.value}\` |`).join('\n');

  return `---
title: Errors
description: Every exception type thrown by the Poli Page .NET SDK, plus the wire-level codes.
---

import ErrorTable from '@preset/components/ErrorTable.astro';

Every failure thrown by the SDK is a \`PoliPageException\` or a subclass. The base type exposes \`Code\`, \`StatusCode\`, \`Message\`, and \`RequestId\`. Compare \`Code\` against the \`PoliPageErrorCode\` constants for fine-grained branching when the subclass is too coarse.

## Exception hierarchy

<ErrorTable errors={${JSON.stringify(rows)}} />

## Wire-level codes

These constants live on the static \`PoliPageErrorCode\` class. The API codes are the exact strings the API returns in its JSON error body (the \`error\` field); the lowercase codes and \`DOWNLOAD_FAILED\` and \`UNKNOWN\` are raised by the SDK itself. \`Code\` carries any other code the API returns, verbatim.

\`\`\`csharp
using PoliPage;

catch (PoliPageException ex) when (ex.Code == PoliPageErrorCode.QuotaExceeded)
{
    // bespoke handling.
}
\`\`\`

| Constant | Value |
|---|---|
${codeRows}
`;
}
