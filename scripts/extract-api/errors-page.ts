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

Every failure thrown by the SDK is a \`PoliPageException\` or a subclass: API errors, network failures, timeouts, a cancellation (\`aborted\`), download failures and constructor validation. The base type exposes \`Code\`, \`StatusCode\`, \`Message\`, and \`RequestId\`. Compare \`Code\` against the \`PoliPageErrorCode\` constants for fine-grained branching when the subclass is too coarse.

## Exception hierarchy

<ErrorTable errors={${JSON.stringify(rows)}} />

## Wire-level codes

These constants live on the static \`PoliPageErrorCode\` class. The API codes are the exact strings the API returns in its JSON error body (the \`error\` field); \`invalid_options\`, \`timeout\`, \`network_error\`, \`aborted\`, \`DOWNLOAD_FAILED\` and \`UNKNOWN\` are raised by the SDK itself; \`aborted\` is a cancellation of your own \`CancellationToken\` (see [Cancellation](../../production/cancellation/)). \`Code\` carries any other code the API returns, verbatim.

\`IsRetryable()\` is \`true\` for \`network_error\`, \`timeout\`, a 5xx and a 429, except \`QUOTA_EXCEEDED\`, \`OVERAGE_CAP_EXCEEDED\` and \`STORAGE_ACCESS_DENIED\`, which waiting cannot fix; it is never \`true\` for \`aborted\` or \`DOWNLOAD_FAILED\`. The SDK retries by the same rule (see [Retries and idempotency](../../production/retries-and-idempotency/)).

\`\`\`csharp
using PoliPage;

catch (PoliPageException ex) when (ex.Code == PoliPageErrorCode.QuotaExceeded)
{
    // Not retried by the SDK: the quota lasts until the monthly reset.
}
\`\`\`

| Constant | Value |
|---|---|
${codeRows}
`;
}
