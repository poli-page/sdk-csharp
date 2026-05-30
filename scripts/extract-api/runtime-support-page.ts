export function buildRuntimeSupportPage(packageVersion: string): string {
  return `---
title: Runtime support
description: Supported .NET versions and operating systems for PoliPage v${packageVersion}.
---

import RuntimeMatrix from '@preset/components/RuntimeMatrix.astro';

The .NET SDK is built and tested against the matrix below.

<RuntimeMatrix matrix={{
  runtimes: ['net8.0', 'net10.0'],
  os: ['linux', 'macos', 'windows'],
  cells: {
    'net8.0':  { linux: 'tested',    macos: 'supported', windows: 'supported' },
    'net10.0': { linux: 'tested',    macos: 'tested',    windows: 'tested' },
  },
}} />

The minimum supported target framework is **.NET 8 LTS** (supported through November 2026). The SDK also tests against **.NET 10 LTS** (supported through November 2028).

## Browsers (Blazor WebAssembly)

Not supported. API keys (\`pp_test_*\`, \`pp_live_*\`) are secrets and must never ship to a browser. Call the SDK from a backend (ASP.NET Core, Worker Service, Azure Function, AWS Lambda) and proxy the result. Blazor Server is fine because the SDK runs on the server.
`;
}
