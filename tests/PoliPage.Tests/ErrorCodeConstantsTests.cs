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

    [Fact]
    public void Does_not_expose_invented_RateLimit_or_Unauthorized_or_Validation()
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
    }
}
