import type { PublicException } from './registry.js';
import type { XmlDocMember } from './xml-docs.js';

export function buildErrorsPage(
  exceptions: readonly PublicException[],
  xml: ReadonlyMap<string, XmlDocMember>,
): string {
  const rows = exceptions.map((e) => {
    const doc = xml.get(e.memberId);
    return {
      code: e.name,
      when: doc?.summary || `${e.name} — see source for details.`,
      recovery: `HTTP ${e.statusCode}. Codes: ${e.codes.join(', ')}.`,
    };
  });

  return `---
title: Errors
description: Every exception type thrown by the Poli Page .NET SDK, plus the wire-level codes.
---

import ErrorTable from '@preset/components/ErrorTable.astro';

Every failure thrown by the SDK is a \`PoliPageException\` or a subclass. The base type exposes \`Code\`, \`StatusCode\`, \`Message\`, and \`RequestId\`. Compare \`Code\` against the \`PoliPageErrorCode\` constants for fine-grained branching when the subclass is too coarse.

## Exception hierarchy

<ErrorTable errors={${JSON.stringify(rows)}} />

## Wire-level codes

These constants live on the static \`PoliPageErrorCode\` class. They are the exact strings returned by the API in the JSON error envelope's \`code\` field.

\`\`\`csharp
using PoliPage;

catch (PoliPageException ex) when (ex.Code == PoliPageErrorCode.QuotaExceeded)
{
    // bespoke handling.
}
\`\`\`

| Constant | Value |
|---|---|
| \`Unauthorized\` | \`UNAUTHORIZED\` |
| \`Forbidden\` | \`FORBIDDEN\` |
| \`NotFound\` | \`NOT_FOUND\` |
| \`VersionNotFound\` | \`VERSION_NOT_FOUND\` |
| \`DocumentNotFound\` | \`DOCUMENT_NOT_FOUND\` |
| \`Gone\` | \`GONE\` |
| \`Validation\` | \`VALIDATION\` |
| \`RateLimit\` | \`RATE_LIMIT\` |
| \`Timeout\` | \`TIMEOUT\` |
| \`Network\` | \`NETWORK\` |
| \`DownloadFailed\` | \`DOWNLOAD_FAILED\` |
| \`PaymentRequired\` | \`PAYMENT_REQUIRED\` |
| \`OrganizationCancelled\` | \`ORGANIZATION_CANCELLED\` |
| \`OrganizationPurged\` | \`ORGANIZATION_PURGED\` |
| \`QuotaExceeded\` | \`QUOTA_EXCEEDED\` |
| \`OverageCapExceeded\` | \`OVERAGE_CAP_EXCEEDED\` |
| \`InvalidVersionFormat\` | \`INVALID_VERSION_FORMAT\` |
| \`VersionRequired\` | \`VERSION_REQUIRED\` |
| \`InvalidVersionForKeyEnv\` | \`INVALID_VERSION_FOR_KEY_ENV\` |
| \`StorageRequired\` | \`STORAGE_REQUIRED\` |
| \`Unknown\` | \`UNKNOWN\` |
`;
}
