import type { PublicException, PublicMethod } from './registry.js';

export function buildMetaSidecar(
  packageVersion: string,
  methods: readonly PublicMethod[],
  exceptions: readonly PublicException[],
): unknown {
  return {
    language: 'csharp',
    package: { kind: 'nuget', name: 'PoliPage', version: packageVersion },
    extractedAt: new Date().toISOString(),
    extractorVersion: '0.1.0',
    client: { name: 'PoliPageClient', kind: 'class' },
    methods: methods.map((m) => ({ slug: m.slug, name: m.displayName })),
    errors: exceptions.map((e) => ({ name: e.name, codes: e.codes })),
  };
}
