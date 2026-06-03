namespace PoliPage;

/// <summary>
/// Wire-level error code constants returned by the Poli Page API in the
/// <c>code</c> field of the JSON error envelope.
/// Use these constants instead of raw strings to guard against typos and to
/// keep your code legible when switching on <see cref="PoliPageException.Code"/>.
/// </summary>
public static class PoliPageErrorCode
{
    /// <summary>No <c>Authorization</c> header was supplied (HTTP 401).</summary>
    public const string MissingApiKey = "MISSING_API_KEY";

    /// <summary>The supplied API key is not recognised (HTTP 401).</summary>
    public const string InvalidApiKey = "INVALID_API_KEY";

    /// <summary>The API key does not have permission to perform this action.</summary>
    public const string Forbidden = "FORBIDDEN";

    /// <summary>The requested resource does not exist.</summary>
    public const string NotFound = "NOT_FOUND";

    /// <summary>The requested template version does not exist.</summary>
    public const string VersionNotFound = "VERSION_NOT_FOUND";

    /// <summary>The requested document does not exist.</summary>
    public const string DocumentNotFound = "DOCUMENT_NOT_FOUND";

    /// <summary>The requested resource has been permanently removed.</summary>
    public const string Gone = "GONE";

    /// <summary>One or more request parameters failed validation (HTTP 400).</summary>
    public const string ValidationError = "VALIDATION_ERROR";

    /// <summary>A required data field is missing from the request body.</summary>
    public const string MissingData = "MISSING_DATA";

    /// <summary>Neither a project nor a template was supplied (one is required).</summary>
    public const string MissingProjectOrTemplate = "MISSING_PROJECT_OR_TEMPLATE";

    /// <summary>A template slug is required but was not provided.</summary>
    public const string MissingTemplateSlug = "MISSING_TEMPLATE_SLUG";

    /// <summary>A project identifier is required when rendering a document.</summary>
    public const string ProjectRequiredForDocument = "PROJECT_REQUIRED_FOR_DOCUMENT";

    // RATE_LIMIT removed — the API uses QuotaExceeded / OverageCapExceeded with HTTP 429.
    // Use PoliPageException.IsRateLimitError() to check by status instead of by code.

    // ------------------------------------------------------------------ //
    // SDK-internal codes (lowercase, matching reference SDK wire spelling)
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

    /// <summary>The account has an outstanding balance that must be settled.</summary>
    public const string PaymentRequired = "PAYMENT_REQUIRED";

    /// <summary>The organisation's subscription has been cancelled.</summary>
    public const string OrganizationCancelled = "ORGANIZATION_CANCELLED";

    /// <summary>The organisation has been purged and all data removed.</summary>
    public const string OrganizationPurged = "ORGANIZATION_PURGED";

    /// <summary>The account has exceeded its included usage quota for the billing period.</summary>
    public const string QuotaExceeded = "QUOTA_EXCEEDED";

    /// <summary>The account has exceeded the overage cap configured on the subscription.</summary>
    public const string OverageCapExceeded = "OVERAGE_CAP_EXCEEDED";

    /// <summary>The version string supplied does not follow the required format.</summary>
    public const string InvalidVersionFormat = "INVALID_VERSION_FORMAT";

    /// <summary>A version is required but was not provided.</summary>
    public const string VersionRequired = "VERSION_REQUIRED";

    /// <summary>The version is not valid for the environment associated with the API key.</summary>
    public const string InvalidVersionForKeyEnv = "INVALID_VERSION_FOR_KEY_ENV";

    /// <summary>The operation requires storage to be configured on the account.</summary>
    public const string StorageRequired = "STORAGE_REQUIRED";

    /// <summary>
    /// Generic fallback used when the API response contains no recognised <c>code</c> field,
    /// or when the error originated outside the API (e.g. a proxy or load-balancer).
    /// </summary>
    public const string Unknown = "UNKNOWN";
}
