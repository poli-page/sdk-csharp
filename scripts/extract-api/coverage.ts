import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import type { PublicMethod } from './registry.js';

// The classes whose public async methods are the SDK's operations.
const SOURCE_FILES: ReadonlyArray<{ readonly className: string; readonly file: string }> = [
  { className: 'Render', file: 'Render.cs' },
  { className: 'Documents', file: 'Documents.cs' },
  { className: 'PoliPageClient', file: 'PoliPageClient.cs' },
];

/** `Class.Method` for every public method returning a Task in the source. */
export function findPublicOperations(repoRoot: string): string[] {
  const found: string[] = [];
  for (const { className, file } of SOURCE_FILES) {
    const src = readFileSync(join(repoRoot, 'src', 'PoliPage', file), 'utf8');
    const re = /^\s*public\s+(?:async\s+)?Task(?:<.*?>)?\s+(\w+)\s*\(/gm;
    for (const m of src.matchAll(re)) found.push(`${className}.${m[1]}`);
  }
  return found;
}

/**
 * Fails the build when the source has a public operation the registry does not
 * document, or the registry documents one the source lacks. Without it a new
 * method ships with no reference page and nothing notices.
 */
export function assertMethodCoverage(
  methods: readonly PublicMethod[],
  repoRoot: string,
): void {
  const documented = new Set(
    methods.map((m) => {
      const id = /^M:PoliPage\.(?:(\w+)\.)?(\w+)\(/.exec(m.memberId);
      if (!id) throw new Error(`registry: malformed memberId ${m.memberId}`);
      return `${id[1] ?? ''}.${id[2]}`;
    }),
  );
  const actual = new Set(findPublicOperations(repoRoot));
  const missing = [...actual].filter((o) => !documented.has(o));
  const unknown = [...documented].filter((o) => !actual.has(o));
  if (missing.length > 0 || unknown.length > 0) {
    throw new Error(
      `extractor: registry out of step with src/PoliPage. ` +
        `Public but undocumented: [${missing.join(', ')}]. ` +
        `Documented but not in source: [${unknown.join(', ')}].`,
    );
  }
}

/** Fails the build when a registry error code is not a PoliPageErrorCode value. */
export function assertErrorCodesExist(
  methods: readonly PublicMethod[],
  errorCodeSource: string,
): void {
  const known = new Set(
    [...errorCodeSource.matchAll(/public const string \w+ = "([^"]+)"/g)].map((m) => m[1]),
  );
  const bad = methods.flatMap((m) =>
    m.errorCodes.filter((c) => !known.has(c)).map((c) => `${m.slug}: ${c}`),
  );
  if (bad.length > 0) {
    throw new Error(`extractor: error codes not in PoliPageErrorCode.cs: ${bad.join('; ')}`);
  }
}
