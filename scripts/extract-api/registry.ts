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

// Wire-level codes (PoliPageErrorCode constants) each method can surface. Every
// entry is checked against PoliPageErrorCode.cs by `assertErrorCodesExist`.
const COMMON_ERRORS = [
  'MISSING_API_KEY',
  'INVALID_API_KEY',
  'FORBIDDEN',
  'RATE_LIMIT_EXCEEDED',
  'timeout',
  'network_error',
  'aborted',
  'UNKNOWN',
] as const;
const RENDER_ERRORS = [
  ...COMMON_ERRORS,
  'PROJECT_NOT_FOUND',
  'TEMPLATE_NOT_FOUND',
  'VALIDATION_ERROR',
  'TEMPLATE_UNSAFE',
  'invalid_response',
] as const;
const RENDER_DOWNLOAD_ERRORS = [...RENDER_ERRORS, 'DOWNLOAD_FAILED'] as const;
const DOCUMENT_ERRORS = [...COMMON_ERRORS, 'DOCUMENT_NOT_FOUND'] as const;

const INPUT_PARAM: PublicParam = {
  name: 'input',
  type: 'RenderInput',
  required: true,
  description:
    'The project, template and data to render (Project + Template + Data), plus optional Metadata, Format, Orientation and Locale.',
};
const DOCUMENT_ID_PARAM: PublicParam = {
  name: 'documentId',
  type: 'string',
  required: true,
  description: 'The server-issued document identifier (e.g. `doc_…`).',
};
const OPTIONS_PARAM: PublicParam = {
  name: 'options',
  type: 'RequestOptions?',
  required: false,
  description: 'Per-call overrides (IdempotencyKey, RequestTimeout, extra headers). Defaults to null.',
  defaultValue: 'null',
};
const CANCEL_PARAM: PublicParam = {
  name: 'cancellationToken',
  type: 'CancellationToken',
  required: false,
  description: 'Token to cancel the operation.',
  defaultValue: 'default',
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
    parameters: [INPUT_PARAM, OPTIONS_PARAM, CANCEL_PARAM],
    returns: '`Task<byte[]>` — the raw PDF bytes.',
    errorCodes: RENDER_DOWNLOAD_ERRORS,
  },
  {
    slug: 'render-pdf-stream',
    displayName: 'client.Render.PdfStreamAsync',
    memberId:
      'M:PoliPage.Render.PdfStreamAsync(PoliPage.RenderInput,PoliPage.RequestOptions,System.Threading.CancellationToken)',
    exampleFile: 'render-pdf-stream.cs',
    signature:
      'Task<Stream> PdfStreamAsync(RenderInput input, RequestOptions? options = null, CancellationToken cancellationToken = default)',
    parameters: [INPUT_PARAM, OPTIONS_PARAM, CANCEL_PARAM],
    returns:
      '`Task<Stream>` — a read-only stream of the PDF. It owns the underlying response: dispose it.',
    errorCodes: RENDER_DOWNLOAD_ERRORS,
  },
  {
    slug: 'render-document',
    displayName: 'client.Render.DocumentAsync',
    memberId:
      'M:PoliPage.Render.DocumentAsync(PoliPage.RenderInput,PoliPage.RequestOptions,System.Threading.CancellationToken)',
    exampleFile: 'render-document.cs',
    signature:
      'Task<DocumentDescriptor> DocumentAsync(RenderInput input, RequestOptions? options = null, CancellationToken cancellationToken = default)',
    parameters: [INPUT_PARAM, OPTIONS_PARAM, CANCEL_PARAM],
    returns:
      '`Task<DocumentDescriptor>` — the stored document, with a short-lived `PresignedPdfUrl`.',
    errorCodes: RENDER_ERRORS,
  },
  {
    slug: 'render-to-file',
    displayName: 'client.RenderToFileAsync',
    memberId:
      'M:PoliPage.PoliPageClient.RenderToFileAsync(PoliPage.RenderInput,System.String,PoliPage.RequestOptions,System.Threading.CancellationToken)',
    exampleFile: 'render-to-file.cs',
    signature:
      'Task RenderToFileAsync(RenderInput input, string path, RequestOptions? options = null, CancellationToken cancellationToken = default)',
    parameters: [
      INPUT_PARAM,
      {
        name: 'path',
        type: 'string',
        required: true,
        description: 'Destination file path. Existing files are overwritten.',
      },
      OPTIONS_PARAM,
      CANCEL_PARAM,
    ],
    returns: '',
    errorCodes: [...RENDER_DOWNLOAD_ERRORS, 'io_error'],
  },
  {
    slug: 'documents-get',
    displayName: 'client.Documents.GetAsync',
    memberId:
      'M:PoliPage.Documents.GetAsync(System.String,PoliPage.RequestOptions,System.Threading.CancellationToken)',
    exampleFile: 'documents-get.cs',
    signature:
      'Task<DocumentDescriptor> GetAsync(string documentId, RequestOptions? options = null, CancellationToken cancellationToken = default)',
    parameters: [DOCUMENT_ID_PARAM, OPTIONS_PARAM, CANCEL_PARAM],
    returns: '`Task<DocumentDescriptor>` — the document, with a fresh `PresignedPdfUrl`.',
    errorCodes: [...DOCUMENT_ERRORS, 'DOCUMENT_GONE', 'invalid_response'],
  },
  {
    slug: 'documents-delete',
    displayName: 'client.Documents.DeleteAsync',
    memberId:
      'M:PoliPage.Documents.DeleteAsync(System.String,PoliPage.RequestOptions,System.Threading.CancellationToken)',
    exampleFile: 'documents-delete.cs',
    signature:
      'Task DeleteAsync(string documentId, RequestOptions? options = null, CancellationToken cancellationToken = default)',
    parameters: [DOCUMENT_ID_PARAM, OPTIONS_PARAM, CANCEL_PARAM],
    returns: '',
    errorCodes: DOCUMENT_ERRORS,
  },
  {
    slug: 'documents-preview',
    displayName: 'client.Documents.PreviewAsync',
    memberId:
      'M:PoliPage.Documents.PreviewAsync(System.String,PoliPage.RequestOptions,System.Threading.CancellationToken)',
    exampleFile: 'documents-preview.cs',
    signature:
      'Task<DocumentPreviewResult> PreviewAsync(string documentId, RequestOptions? options = null, CancellationToken cancellationToken = default)',
    parameters: [DOCUMENT_ID_PARAM, OPTIONS_PARAM, CANCEL_PARAM],
    returns: '`Task<DocumentPreviewResult>` — the HTML body (`Html`) and the page count (`PageCount`).',
    errorCodes: [...DOCUMENT_ERRORS, 'DOCUMENT_GONE'],
  },
  {
    slug: 'documents-thumbnails',
    displayName: 'client.Documents.ThumbnailsAsync',
    memberId:
      'M:PoliPage.Documents.ThumbnailsAsync(System.String,PoliPage.ThumbnailOptions,PoliPage.RequestOptions,System.Threading.CancellationToken)',
    exampleFile: 'documents-thumbnails.cs',
    signature:
      'Task<IReadOnlyList<Thumbnail>> ThumbnailsAsync(string documentId, ThumbnailOptions? thumbnailOptions = null, RequestOptions? options = null, CancellationToken cancellationToken = default)',
    parameters: [
      DOCUMENT_ID_PARAM,
      {
        name: 'thumbnailOptions',
        type: 'ThumbnailOptions?',
        required: false,
        description:
          'Optional Width, Format, Quality and Pages. Null, or options with nothing set, sends `{}` so every API default applies.',
        defaultValue: 'null',
      },
      OPTIONS_PARAM,
      CANCEL_PARAM,
    ],
    returns: '`Task<IReadOnlyList<Thumbnail>>` — one `Thumbnail` per page of the source document.',
    errorCodes: [...DOCUMENT_ERRORS, 'THUMBNAILS_NOT_AVAILABLE', 'invalid_response'],
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
  { name: 'PoliPageValidationException',      memberId: 'T:PoliPage.PoliPageValidationException',      statusCode: '400 / 422', codes: ['VALIDATION_ERROR', 'ENVIRONMENT_MISMATCH', 'TEMPLATE_ERROR', 'TEMPLATE_UNSAFE', 'EMPTY_DOCUMENT'] },
  { name: 'PoliPageRateLimitException',       memberId: 'T:PoliPage.PoliPageRateLimitException',       statusCode: '429',       codes: ['RATE_LIMIT_EXCEEDED', 'QUOTA_EXCEEDED', 'OVERAGE_CAP_EXCEEDED'] },
  { name: 'PoliPageNetworkException',         memberId: 'T:PoliPage.PoliPageNetworkException',         statusCode: '0',         codes: ['network_error'] },
  { name: 'PoliPageDownloadException',        memberId: 'T:PoliPage.PoliPageDownloadException',        statusCode: 'storage',   codes: ['DOWNLOAD_FAILED'] },
];
