// Static registry of the public surface we ship docs for.
//
// Why a registry instead of a pure code-driven extractor: the .NET XML doc
// pipeline emits one <member> per public symbol with no structural grouping,
// so an MDX-shaped reference needs a hand-curated list of "what to surface"
// anyway. The XML supplies the prose; the registry supplies the topology.

export interface PublicMethod {
  /** Kebab-case slug used in `reference/methods/<slug>.mdx`. */
  readonly slug: string;
  /** Human-facing display name (e.g. `client.Render.PdfAsync`). */
  readonly displayName: string;
  /** XML doc member id, e.g. `M:PoliPage.Render.PdfAsync(PoliPage.RenderInput,PoliPage.RequestOptions,System.Threading.CancellationToken)`. */
  readonly memberId: string;
  /** File under `examples/` to embed in `## Example`. Missing file fails the build. */
  readonly exampleFile: string;
  /** Canonical method signature emitted into `<MethodSignature>`. */
  readonly signature: string;
  /** Parameter rows for `<ParamsTable>`. */
  readonly parameters: readonly PublicParam[];
  /** Return-type string for `## Returns`. Pass empty to omit the section. */
  readonly returns: string;
  /** Wire-level error codes for `<ErrorTable>`. */
  readonly errorCodes: readonly string[];
}

export interface PublicParam {
  readonly name: string;
  readonly type: string;
  readonly required: boolean;
  readonly description: string;
  readonly defaultValue?: string;
}

export interface PublicType {
  readonly name: string;
  readonly memberId: string;
  readonly kind: 'class' | 'record' | 'interface' | 'enum';
}

export interface PublicException {
  readonly name: string;
  readonly memberId: string;
  /** HTTP status range this exception covers, e.g. `401 / 403`. */
  readonly statusCode: string;
  /** Wire-level codes (PoliPageErrorCode constants) routed to this subclass. */
  readonly codes: readonly string[];
}

export const CLIENT_TYPE: PublicType = {
  name: 'PoliPageClient',
  memberId: 'T:PoliPage.PoliPageClient',
  kind: 'class',
};

export const PUBLIC_METHODS: readonly PublicMethod[] = [
  {
    slug: 'render-pdf',
    displayName: 'client.Render.PdfAsync',
    memberId:
      'M:PoliPage.Render.PdfAsync(PoliPage.RenderInput,PoliPage.RequestOptions,System.Threading.CancellationToken)',
    exampleFile: 'render-pdf.cs',
    signature:
      'Task<byte[]> PdfAsync(RenderInput input, RequestOptions? options = null, CancellationToken cancellationToken = default)',
    parameters: [
      {
        name: 'input',
        type: 'RenderInput',
        required: true,
        description: 'The project, template and data to render (Project + Template + Data), plus optional Metadata, Format, Orientation and Locale.',
      },
      {
        name: 'options',
        type: 'RequestOptions?',
        required: false,
        description: 'Per-call overrides (IdempotencyKey, RequestTimeout, extra headers). Defaults to null.',
        defaultValue: 'null',
      },
      {
        name: 'cancellationToken',
        type: 'CancellationToken',
        required: false,
        description: 'Token to cancel the operation.',
        defaultValue: 'default',
      },
    ],
    returns: '`Task<byte[]>` — the raw PDF bytes.',
    errorCodes: [
      'UNAUTHORIZED',
      'NOT_FOUND',
      'VALIDATION',
      'RATE_LIMIT',
      'TIMEOUT',
      'NETWORK',
      'UNKNOWN',
    ],
  },
];

export const PUBLIC_TYPES: readonly PublicType[] = [
  { name: 'PoliPageClient',         memberId: 'T:PoliPage.PoliPageClient',         kind: 'class' },
  { name: 'PoliPageClientOptions',  memberId: 'T:PoliPage.PoliPageClientOptions',  kind: 'record' },
  { name: 'Render',                 memberId: 'T:PoliPage.Render',                 kind: 'class' },
  { name: 'RequestOptions',         memberId: 'T:PoliPage.RequestOptions',         kind: 'record' },
  { name: 'RetryEvent',             memberId: 'T:PoliPage.RetryEvent',             kind: 'record' },
  { name: 'RenderInput',            memberId: 'T:PoliPage.RenderInput',            kind: 'record' },
  { name: 'RenderMetadata',         memberId: 'T:PoliPage.RenderMetadata',         kind: 'class' },
  { name: 'PoliPageErrorCode',      memberId: 'T:PoliPage.PoliPageErrorCode',      kind: 'class' },
];

export const PUBLIC_EXCEPTIONS: readonly PublicException[] = [
  { name: 'PoliPageException',                memberId: 'T:PoliPage.PoliPageException',                statusCode: 'any',       codes: ['*'] },
  { name: 'PoliPageAuthException',            memberId: 'T:PoliPage.PoliPageAuthException',            statusCode: '401 / 403', codes: ['MISSING_API_KEY', 'INVALID_API_KEY', 'FORBIDDEN', 'ORGANIZATION_PAYMENT_GRACE', 'ENVIRONMENT_PROTECTED', 'THUMBNAILS_NOT_AVAILABLE'] },
  { name: 'PoliPageNotFoundException',        memberId: 'T:PoliPage.PoliPageNotFoundException',        statusCode: '404',       codes: ['NOT_FOUND', 'PROJECT_NOT_FOUND', 'PROJECT_NOT_IN_ENVIRONMENT', 'TEMPLATE_NOT_FOUND', 'DOCUMENT_NOT_FOUND'] },
  { name: 'PoliPageGoneException',            memberId: 'T:PoliPage.PoliPageGoneException',            statusCode: '410',       codes: ['DOCUMENT_GONE'] },
  { name: 'PoliPageValidationException',      memberId: 'T:PoliPage.PoliPageValidationException',      statusCode: '400 / 422', codes: ['VALIDATION_ERROR', 'ENVIRONMENT_MISMATCH', 'TEMPLATE_ERROR', 'EMPTY_DOCUMENT'] },
  { name: 'PoliPageRateLimitException',       memberId: 'T:PoliPage.PoliPageRateLimitException',       statusCode: '429',       codes: ['RATE_LIMIT_EXCEEDED', 'QUOTA_EXCEEDED', 'OVERAGE_CAP_EXCEEDED'] },
  { name: 'PoliPageNetworkException',         memberId: 'T:PoliPage.PoliPageNetworkException',         statusCode: '0',         codes: ['network_error'] },
  { name: 'PoliPageDownloadException',        memberId: 'T:PoliPage.PoliPageDownloadException',        statusCode: 'storage',   codes: ['DOWNLOAD_FAILED'] },
];
