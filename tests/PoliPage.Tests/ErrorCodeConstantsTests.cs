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
    public void Has_MissingProjectOrTemplate_with_canonical_wire_value()
    {
        PoliPageErrorCode.MissingProjectOrTemplate.Should().Be("MISSING_PROJECT_OR_TEMPLATE");
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
