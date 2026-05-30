import { execSync } from 'node:child_process';
import {
  readFileSync,
  writeFileSync,
  mkdirSync,
  rmSync,
  existsSync,
} from 'node:fs';
import { join, dirname, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { parseXmlDocs, type XmlDocMember } from './xml-docs.js';
import {
  PUBLIC_METHODS,
  PUBLIC_TYPES,
  PUBLIC_EXCEPTIONS,
  CLIENT_TYPE,
} from './registry.js';
import { buildClientPage } from './client-page.js';
import { buildMethodPages } from './method-pages.js';
import { buildTypesPage } from './types-page.js';
import { buildErrorsPage } from './errors-page.js';
import { buildRuntimeSupportPage } from './runtime-support-page.js';
import { buildMetaSidecar } from './meta-sidecar.js';

const HERE = dirname(fileURLToPath(import.meta.url));
const REPO_ROOT = resolve(HERE, '..', '..');
const REFERENCE_OUT = resolve(
  REPO_ROOT,
  'docs',
  'src',
  'content',
  'docs',
  'reference',
);
const XML_CACHE = resolve(HERE, '.cache', 'PoliPage.xml');

interface DirectoryBuildProps {
  readonly version: string;
}

function readVersion(): string {
  // Directory.Build.props carries <Version>X.Y.Z</Version>. Cheap regex parse;
  // formal XML parsing would pull a dep for no benefit.
  const src = readFileSync(resolve(REPO_ROOT, 'Directory.Build.props'), 'utf8');
  const m = src.match(/<Version>([^<]+)<\/Version>/);
  return m?.[1]?.trim() ?? '0.0.0';
}

function tryBuildXmlDocs(): XmlDocMember[] | null {
  // Best-effort: try `dotnet build` to produce the XML doc file. When the
  // toolchain is unavailable (CI without setup-dotnet, sandboxed runs), fall
  // back to the static registry. Docs CI runs `setup-dotnet` and gets XML.
  try {
    mkdirSync(dirname(XML_CACHE), { recursive: true });
    execSync(
      `dotnet build src/PoliPage/PoliPage.csproj -c Release ` +
        `-p:DocumentationFile=${XML_CACHE} ` +
        `-p:GenerateDocumentationFile=true ` +
        `-p:TargetFrameworks=net8.0 -p:TargetFramework=net8.0 ` +
        `--nologo -v quiet`,
      { cwd: REPO_ROOT, stdio: 'inherit' },
    );
    if (!existsSync(XML_CACHE)) return null;
    const xml = readFileSync(XML_CACHE, 'utf8');
    return parseXmlDocs(xml);
  } catch (err) {
    console.warn(
      `extractor: dotnet build failed — falling back to static registry. ` +
        `Reason: ${(err as Error).message.split('\n')[0]}`,
    );
    return null;
  }
}

function run(): void {
  const version = readVersion();

  if (existsSync(REFERENCE_OUT)) rmSync(REFERENCE_OUT, { recursive: true, force: true });
  mkdirSync(REFERENCE_OUT, { recursive: true });
  mkdirSync(join(REFERENCE_OUT, 'methods'), { recursive: true });

  const xmlMembers = tryBuildXmlDocs() ?? [];
  const xmlIndex = new Map(xmlMembers.map((m) => [m.name, m]));

  writeFileSync(
    join(REFERENCE_OUT, 'client.mdx'),
    buildClientPage(CLIENT_TYPE, xmlIndex),
    'utf8',
  );
  for (const page of buildMethodPages(PUBLIC_METHODS, REPO_ROOT, xmlIndex)) {
    writeFileSync(join(REFERENCE_OUT, 'methods', `${page.slug}.mdx`), page.mdx, 'utf8');
  }
  writeFileSync(
    join(REFERENCE_OUT, 'types.mdx'),
    buildTypesPage(PUBLIC_TYPES, xmlIndex),
    'utf8',
  );
  writeFileSync(
    join(REFERENCE_OUT, 'errors.mdx'),
    buildErrorsPage(PUBLIC_EXCEPTIONS, xmlIndex),
    'utf8',
  );
  writeFileSync(
    join(REFERENCE_OUT, 'runtime-support.mdx'),
    buildRuntimeSupportPage(version),
    'utf8',
  );
  writeFileSync(
    join(REFERENCE_OUT, '_meta.json'),
    JSON.stringify(buildMetaSidecar(version, PUBLIC_METHODS, PUBLIC_EXCEPTIONS), null, 2) + '\n',
    'utf8',
  );

  console.log(`extractor: wrote ${REFERENCE_OUT}`);
}

run();
