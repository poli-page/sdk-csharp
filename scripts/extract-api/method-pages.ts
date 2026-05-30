import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { PublicMethod } from './registry.js';
import { firstSentence, type XmlDocMember } from './xml-docs.js';

export function buildMethodPages(
  methods: readonly PublicMethod[],
  repoRoot: string,
  xml: ReadonlyMap<string, XmlDocMember>,
): Array<{ slug: string; mdx: string }> {
  const out: Array<{ slug: string; mdx: string }> = [];
  for (const m of methods) {
    const examplePath = join(repoRoot, 'examples', m.exampleFile);
    const example = readFileSync(examplePath, 'utf8');
    const doc = xml.get(m.memberId);
    out.push({ slug: m.slug, mdx: renderMethodPage(m, example, doc) });
  }
  return out;
}

function renderMethodPage(
  method: PublicMethod,
  example: string,
  doc: XmlDocMember | undefined,
): string {
  const summary = doc?.summary || `${method.displayName} method.`;
  const description = firstSentence(summary);

  const parameters = method.parameters.map((p) => ({
    name: p.name,
    type: p.type,
    required: p.required,
    description: doc?.params.get(p.name) || p.description,
    ...(p.defaultValue !== undefined ? { default: p.defaultValue } : {}),
  }));

  const parametersBlock =
    parameters.length === 0
      ? ''
      : `\n## Parameters\n\n<ParamsTable params={${JSON.stringify(parameters)}} />\n`;

  const returnText = doc?.returns || method.returns;
  const returnsBlock = returnText ? `\n## Returns\n\n${returnText}\n` : '';

  const errorRows = method.errorCodes.map((code) => ({
    code,
    when: 'See [errors](../../../production/errors/) for the full description.',
  }));
  const errorsBlock =
    errorRows.length === 0
      ? ''
      : `\n## Errors\n\n<ErrorTable errors={${JSON.stringify(errorRows)}} />\n`;

  return `---
title: ${method.displayName}
description: ${escapeFrontmatter(description || method.displayName)}
sidebar:
  label: ${method.displayName}
---

import MethodSignature from '@preset/components/MethodSignature.astro';
import ParamsTable from '@preset/components/ParamsTable.astro';
import ErrorTable from '@preset/components/ErrorTable.astro';

<MethodSignature lang="csharp" code={\`${method.signature}\`} />

${summary}
${parametersBlock}${returnsBlock}${errorsBlock}
## Example

\`\`\`csharp
${example.trimEnd()}
\`\`\`

## See also
- [Errors](../../../production/errors/)
- [Configuration](../../../concepts/configuration/)
`;
}

function escapeFrontmatter(s: string): string {
  return s.replace(/"/g, '\\"').replace(/\n/g, ' ').slice(0, 150);
}
