using FluentAssertions;
using Xunit;

namespace PoliPage.Tests;

public sealed class ErrorCodeConstantsTests
{
    [Fact]
    public void Has_MissingApiKey_with_canonical_wire_value()
    {
        PoliPageErrorCode.MissingApiKey.Should().Be("MISSING_API_KEY");
    }

    [Fact]
    public void Has_InvalidApiKey_with_canonical_wire_value()
    {
        PoliPageErrorCode.InvalidApiKey.Should().Be("INVALID_API_KEY");
    }

    [Fact]
    public void Has_ValidationError_with_canonical_wire_value()
    {
        PoliPageErrorCode.ValidationError.Should().Be("VALIDATION_ERROR");
    }

    [Fact]
    public void Has_QuotaExceeded_with_canonical_wire_value()
    {
        PoliPageErrorCode.QuotaExceeded.Should().Be("QUOTA_EXCEEDED");
    }

    // ------------------------------------------------------------------ //
    // 8b — validation codes
    // ------------------------------------------------------------------ //

    [Fact]
    public void Has_MissingData_with_canonical_wire_value()
    {
        PoliPageErrorCode.MissingData.Should().Be("MISSING_DATA");
    }

    [Fact]
    public void Has_MissingTemplateSlug_with_canonical_wire_value()
    {
        PoliPageErrorCode.MissingTemplateSlug.Should().Be("MISSING_TEMPLATE_SLUG");
    }

    [Fact]
    public void Has_ProjectRequiredForDocument_with_canonical_wire_value()
    {
        PoliPageErrorCode.ProjectRequiredForDocument.Should().Be("PROJECT_REQUIRED_FOR_DOCUMENT");
    }

    // ------------------------------------------------------------------ //
    // 8c — SDK-internal codes (lowercase wire spelling)
    // ------------------------------------------------------------------ //

    [Fact]
    public void Has_InvalidOptions_with_lowercase_wire_value()
    {
        PoliPageErrorCode.InvalidOptions.Should().Be("invalid_options");
    }

    [Fact]
    public void Has_NetworkError_with_lowercase_wire_value()
    {
        PoliPageErrorCode.NetworkError.Should().Be("network_error");
    }

    [Fact]
    public void Has_Timeout_with_lowercase_wire_value()
    {
        PoliPageErrorCode.Timeout.Should().Be("timeout");
    }

    [Fact]
    public void Has_Aborted_with_lowercase_wire_value()
    {
        PoliPageErrorCode.Aborted.Should().Be("aborted");
    }

    // ------------------------------------------------------------------ //
    // Full known-code set: 31 API codes + 5 SDK codes + Unknown
    // ------------------------------------------------------------------ //

    private static Dictionary<string, string> PublicConstants()
        => typeof(PoliPageErrorCode)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!, StringComparer.Ordinal);

    [Fact]
    public void Known_codes_are_exactly_the_contract_set()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // API codes kept
            ["MissingApiKey"] = "MISSING_API_KEY",
            ["InvalidApiKey"] = "INVALID_API_KEY",
            ["OrganizationCancelled"] = "ORGANIZATION_CANCELLED",
            ["OrganizationPurged"] = "ORGANIZATION_PURGED",
            ["Forbidden"] = "FORBIDDEN",
            ["ValidationError"] = "VALIDATION_ERROR",
            ["MissingData"] = "MISSING_DATA",
            ["MissingTemplateSlug"] = "MISSING_TEMPLATE_SLUG",
            ["ProjectRequiredForDocument"] = "PROJECT_REQUIRED_FOR_DOCUMENT",
            ["NotFound"] = "NOT_FOUND",
            ["DocumentNotFound"] = "DOCUMENT_NOT_FOUND",
            ["QuotaExceeded"] = "QUOTA_EXCEEDED",
            ["OverageCapExceeded"] = "OVERAGE_CAP_EXCEEDED",
            ["InternalError"] = "INTERNAL_ERROR",
            // API codes added by the environments contract
            ["PublishInProgress"] = "PUBLISH_IN_PROGRESS",
            ["EnvironmentMismatch"] = "ENVIRONMENT_MISMATCH",
            ["ProjectNotInEnvironment"] = "PROJECT_NOT_IN_ENVIRONMENT",
            ["TemplateNotFound"] = "TEMPLATE_NOT_FOUND",
            ["DocumentGone"] = "DOCUMENT_GONE",
            ["EnvironmentProtected"] = "ENVIRONMENT_PROTECTED",
            ["OrganizationPaymentGrace"] = "ORGANIZATION_PAYMENT_GRACE",
            ["EmptyDocument"] = "EMPTY_DOCUMENT",
            ["StorageAccessDenied"] = "STORAGE_ACCESS_DENIED",
            ["OrganizationMigrating"] = "ORGANIZATION_MIGRATING",
            ["ProjectNotFound"] = "PROJECT_NOT_FOUND",
            ["TemplateError"] = "TEMPLATE_ERROR",
            ["ThumbnailsNotAvailable"] = "THUMBNAILS_NOT_AVAILABLE",
            ["RateLimitExceeded"] = "RATE_LIMIT_EXCEEDED",
            ["RenderBodyTooLarge"] = "RENDER_BODY_TOO_LARGE",
            ["DocumentTooLarge"] = "DOCUMENT_TOO_LARGE",
            ["RenderTimeout"] = "RENDER_TIMEOUT",
            // SDK codes
            ["InvalidOptions"] = "invalid_options",
            ["NetworkError"] = "network_error",
            ["Timeout"] = "timeout",
            ["Aborted"] = "aborted",
            ["DownloadFailed"] = "DOWNLOAD_FAILED",
            // SDK-internal fallback
            ["Unknown"] = "UNKNOWN",
        };

        PublicConstants().Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData("VERSION_NOT_FOUND")]
    [InlineData("INVALID_VERSION_FORMAT")]
    [InlineData("VERSION_REQUIRED")]
    [InlineData("INVALID_VERSION_FOR_KEY_ENV")]
    [InlineData("MISSING_PROJECT_OR_TEMPLATE")]
    [InlineData("GONE")]
    [InlineData("PAYMENT_REQUIRED")]
    [InlineData("STORAGE_REQUIRED")]
    public void Removed_codes_are_gone(string wireValue)
    {
        PublicConstants().Values.Should().NotContain(wireValue);
    }

    // ------------------------------------------------------------------ //
    // Negative: removed / renamed constants
    // ------------------------------------------------------------------ //

    [Fact]
    public void Does_not_expose_invented_RateLimit_or_Unauthorized_or_Validation_or_Network()
    {
        var fieldNames = typeof(PoliPageErrorCode)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(f => f.Name)
            .ToList();

        fieldNames.Should().NotContain("RateLimit",
            "RATE_LIMIT is not a canonical wire code — the API uses QUOTA_EXCEEDED / OVERAGE_CAP_EXCEEDED");
        fieldNames.Should().NotContain("Unauthorized",
            "UNAUTHORIZED is not canonical — use MissingApiKey / InvalidApiKey");
        fieldNames.Should().NotContain("Validation",
            "VALIDATION is not canonical — use ValidationError");
        fieldNames.Should().NotContain("Network",
            "Network was renamed to NetworkError to match the lowercase 'network_error' wire value");
    }
}
