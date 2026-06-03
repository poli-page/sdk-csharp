using FluentAssertions;
using Xunit;

namespace PoliPage.Tests;

public sealed class PoliPageExceptionPredicateTests
{
    // IsAuthError — true for 401 and 403, false otherwise.
    [Theory]
    [InlineData(401, true)]
    [InlineData(403, true)]
    [InlineData(400, false)]
    [InlineData(404, false)]
    [InlineData(429, false)]
    [InlineData(500, false)]
    [InlineData(0, false)]
    public void IsAuthError_returns_true_only_for_401_or_403(int status, bool expected)
    {
        var ex = new PoliPageException("CODE", status, "msg");

        ex.IsAuthError().Should().Be(expected);
    }

    // IsRateLimitError — true only for 429.
    [Theory]
    [InlineData(429, true)]
    [InlineData(401, false)]
    [InlineData(503, false)]
    [InlineData(0, false)]
    public void IsRateLimitError_returns_true_only_for_429(int status, bool expected)
    {
        var ex = new PoliPageException("CODE", status, "msg");

        ex.IsRateLimitError().Should().Be(expected);
    }

    // IsValidationError — true only for 400.
    [Theory]
    [InlineData(400, true)]
    [InlineData(422, false)]
    [InlineData(401, false)]
    [InlineData(0, false)]
    public void IsValidationError_returns_true_only_for_400(int status, bool expected)
    {
        var ex = new PoliPageException("CODE", status, "msg");

        ex.IsValidationError().Should().Be(expected);
    }

    // IsNetworkError — true when code is "network_error" or "timeout".
    [Theory]
    [InlineData("network_error", true)]
    [InlineData("timeout", true)]
    [InlineData("aborted", false)]
    [InlineData("MISSING_API_KEY", false)]
    [InlineData("INTERNAL_ERROR", false)]
    public void IsNetworkError_returns_true_only_for_network_or_timeout_code(string code, bool expected)
    {
        var ex = new PoliPageException(code, 0, "msg");

        ex.IsNetworkError().Should().Be(expected);
    }

    // IsRetryable — true for 5xx, 429, network, timeout; never for aborted.
    [Theory]
    [InlineData("network_error", 0, true)]
    [InlineData("timeout", 0, true)]
    [InlineData("aborted", 0, false)] // explicit caller cancellation is NEVER retryable
    [InlineData("INTERNAL_ERROR", 500, true)]
    [InlineData("INTERNAL_ERROR", 503, true)]
    [InlineData("QUOTA_EXCEEDED", 429, true)]
    [InlineData("INVALID_API_KEY", 401, false)]
    [InlineData("VALIDATION_ERROR", 400, false)]
    [InlineData("NOT_FOUND", 404, false)]
    public void IsRetryable_matches_node_reference_semantics(string code, int status, bool expected)
    {
        var ex = new PoliPageException(code, status, "msg");

        ex.IsRetryable().Should().Be(expected);
    }
}
