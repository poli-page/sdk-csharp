namespace PoliPage;

/// <summary>
/// Known error codes: the codes the Poli Page API returns in its JSON error
/// body, plus the codes the SDK raises itself.
/// Use these constants instead of raw strings to guard against typos and to
/// keep your code legible when switching on <see cref="PoliPageException.Code"/>.
/// </summary>
/// <remarks>
/// The set is open: <see cref="PoliPageException.Code"/> carries any code the API
/// returns, verbatim, including codes added after this SDK version shipped.
/// </remarks>
public static class PoliPageErrorCode
{
    // ------------------------------------------------------------------ //
    // Authentication
    // ------------------------------------------------------------------ //

    /// <summary>No <c>Authorization</c> header was supplied (HTTP 401).</summary>
    public const string MissingApiKey = "MISSING_API_KEY";

    /// <summary>The supplied API key is not recognised (HTTP 401).</summary>
    public const string InvalidApiKey = "INVALID_API_KEY";

    // ------------------------------------------------------------------ //
    // Organisation and access
    // ------------------------------------------------------------------ //

    /// <summary>The API key does not have permission to perform this action (HTTP 403).</summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>The organisation's subscription has been cancelled.</summary>
    public const string OrganizationCancelled = "ORGANIZATION_CANCELLED";

    /// <summary>The organisation has been purged and all data removed.</summary>
    public const string OrganizationPurged = "ORGANIZATION_PURGED";

    /// <summary>The organisation is in its payment grace period (HTTP 403).</summary>
    public const string OrganizationPaymentGrace = "ORGANIZATION_PAYMENT_GRACE";

    /// <summary>The organisation is being migrated; retry later (HTTP 503).</summary>
    public const string OrganizationMigrating = "ORGANIZATION_MIGRATING";

    /// <summary>The environment is protected against this operation (HTTP 403).</summary>
    public const string EnvironmentProtected = "ENVIRONMENT_PROTECTED";

    /// <summary>Thumbnails are not available on the organisation's plan (HTTP 403).</summary>
    public const string ThumbnailsNotAvailable = "THUMBNAILS_NOT_AVAILABLE";

    // ------------------------------------------------------------------ //
    // Not found / gone
    // ------------------------------------------------------------------ //

    /// <summary>The requested resource does not exist (HTTP 404).</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>The project does not exist (HTTP 404).</summary>
    public const string ProjectNotFound = "PROJECT_NOT_FOUND";

    /// <summary>The project has not been published to the API key's environment (HTTP 404).</summary>
    public const string ProjectNotInEnvironment = "PROJECT_NOT_IN_ENVIRONMENT";

    /// <summary>The template does not exist in the project (HTTP 404).</summary>
    public const string TemplateNotFound = "TEMPLATE_NOT_FOUND";

    /// <summary>The requested document does not exist (HTTP 404).</summary>
    public const string DocumentNotFound = "DOCUMENT_NOT_FOUND";

    /// <summary>The requested document has been deleted (HTTP 410).</summary>
    public const string DocumentGone = "DOCUMENT_GONE";

    // ------------------------------------------------------------------ //
    // Validation
    // ------------------------------------------------------------------ //

    /// <summary>One or more request parameters failed validation, or the body has an unknown field (HTTP 400).</summary>
    public const string ValidationError = "VALIDATION_ERROR";

    /// <summary>A required data field is missing from the request body.</summary>
    public const string MissingData = "MISSING_DATA";

    /// <summary>A template slug is required but was not provided.</summary>
    public const string MissingTemplateSlug = "MISSING_TEMPLATE_SLUG";

    /// <summary>A project identifier is required when rendering a document.</summary>
    public const string ProjectRequiredForDocument = "PROJECT_REQUIRED_FOR_DOCUMENT";

    /// <summary>The request does not match the API key's environment (HTTP 400).</summary>
    public const string EnvironmentMismatch = "ENVIRONMENT_MISMATCH";

    /// <summary>The render produced an empty document (HTTP 400).</summary>
    public const string EmptyDocument = "EMPTY_DOCUMENT";

    /// <summary>The template failed to render with the supplied data (HTTP 400).</summary>
    public const string TemplateError = "TEMPLATE_ERROR";

    /// <summary>The render request body is too large (HTTP 413).</summary>
    public const string RenderBodyTooLarge = "RENDER_BODY_TOO_LARGE";

    /// <summary>The rendered document is too large (HTTP 413).</summary>
    public const string DocumentTooLarge = "DOCUMENT_TOO_LARGE";

    // ------------------------------------------------------------------ //
    // Rate limits and quotas (HTTP 429)
    // ------------------------------------------------------------------ //

    /// <summary>The account has exceeded its included usage quota for the billing period.</summary>
    public const string QuotaExceeded = "QUOTA_EXCEEDED";

    /// <summary>The account has exceeded the overage cap configured on the subscription.</summary>
    public const string OverageCapExceeded = "OVERAGE_CAP_EXCEEDED";

    /// <summary>Too many requests in a short period (HTTP 429).</summary>
    public const string RateLimitExceeded = "RATE_LIMIT_EXCEEDED";

    // ------------------------------------------------------------------ //
    // Server
    // ------------------------------------------------------------------ //

    /// <summary>The API failed unexpectedly, or answered with a body that is not JSON.</summary>
    public const string InternalError = "INTERNAL_ERROR";

    /// <summary>The API could not access the organisation's storage (HTTP 502).</summary>
    public const string StorageAccessDenied = "STORAGE_ACCESS_DENIED";

    /// <summary>
    /// A publish is in progress for this project and environment (HTTP 503, with
    /// <c>Retry-After</c>). Retried automatically like any other 5xx.
    /// </summary>
    public const string PublishInProgress = "PUBLISH_IN_PROGRESS";

    /// <summary>The render did not finish in time (HTTP 504).</summary>
    public const string RenderTimeout = "RENDER_TIMEOUT";

    // ------------------------------------------------------------------ //
    // SDK codes (raised by the SDK, not returned by the API)
    // ------------------------------------------------------------------ //

    /// <summary>The options or parameters passed to the SDK call are invalid.</summary>
    public const string InvalidOptions = "invalid_options";

    /// <summary>The request timed out before the server responded.</summary>
    public const string Timeout = "timeout";

    /// <summary>A DNS, TCP, or TLS failure prevented the request from reaching the server.</summary>
    public const string NetworkError = "network_error";

    /// <summary>The in-flight request was aborted by the caller.</summary>
    public const string Aborted = "aborted";

    /// <summary>Downloading the generated file from remote storage failed.</summary>
    public const string DownloadFailed = "DOWNLOAD_FAILED";

    /// <summary>
    /// Fallback for SDK-internal errors that have no more specific code, such as a
    /// 2xx response with an empty body or an exception built without a code.
    /// </summary>
    public const string Unknown = "UNKNOWN";
}
