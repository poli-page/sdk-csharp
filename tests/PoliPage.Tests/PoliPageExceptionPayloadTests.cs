using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace PoliPage.Tests;

public sealed class PoliPageExceptionPayloadTests
{
    [Fact]
    public void ToPayload_uses_api_status_for_status_bearing_exception()
    {
        var ex = new PoliPageAuthException(
            code: "authentication_failed",
            statusCode: 401,
            message: "Forbidden",
            requestId: "req_abc");

        var payload = ex.ToPayload();

        payload.Code.Should().Be("authentication_failed");
        payload.Message.Should().Be("Forbidden");
        payload.Status.Should().Be(401);
        payload.RequestId.Should().Be("req_abc");
    }

    [Fact]
    public void ToPayload_uses_503_for_network_exception()
    {
        var ex = new PoliPageNetworkException(
            PoliPageErrorCode.NetworkError, "dns failure", new HttpRequestException("dns"));

        var payload = ex.ToPayload();

        payload.Status.Should().Be(503);
        ex.StatusCode.Should().Be(0, "transport errors keep StatusCode=0 — only the payload surfaces 503");
    }

    [Fact]
    public void ToPayload_uses_504_for_base_exception_with_timeout_code()
    {
        var ex = new PoliPageException(
            code: PoliPageErrorCode.Timeout,
            statusCode: 0,
            message: "deadline exceeded");

        var payload = ex.ToPayload();

        payload.Status.Should().Be(504);
        ex.StatusCode.Should().Be(0);
    }

    [Fact]
    public void ToPayload_request_id_is_null_when_absent()
    {
        var ex = new PoliPageNetworkException("dns failure");

        ex.ToPayload().RequestId.Should().BeNull();
    }

    [Fact]
    public void ToPayload_status_is_null_for_bare_base_exception_without_timeout_code()
    {
        var ex = new PoliPageException("config error");

        ex.ToPayload().Status.Should().BeNull();
    }

    [Fact]
    public void ToPayload_uses_502_for_invalid_response_and_keeps_the_2xx_on_the_error()
    {
        var ex = new PoliPageInvalidResponseException(
            PoliPageErrorCode.InvalidResponse, 200, "Invalid response: HTTP 200 body is not JSON", "req_x");

        ex.ToPayload().Status.Should().Be(502);
        ex.StatusCode.Should().Be(200);
        ex.IsRetryable().Should().BeFalse();
    }

    [Fact]
    public void ToPayload_uses_502_for_any_exception_with_the_invalid_response_code()
    {
        var ex = new PoliPageException(PoliPageErrorCode.InvalidResponse, 204, "Invalid response: HTTP 204 body is not JSON");

        ex.ToPayload().Status.Should().Be(502);
        ex.IsRetryable().Should().BeFalse();
    }

    public static TheoryData<JsonNamingPolicy?> NamingPolicies => new()
    {
        null,
        JsonNamingPolicy.CamelCase,
        JsonNamingPolicy.SnakeCaseLower,
        JsonNamingPolicy.KebabCaseUpper,
    };

    [Theory]
    [MemberData(nameof(NamingPolicies))]
    public void ToPayload_serializes_with_exactly_the_four_contract_keys(JsonNamingPolicy? policy)
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = policy };
        var withRequestId = new PoliPageAuthException("INVALID_API_KEY", 401, "Invalid API key", "req_abc").ToPayload();
        var withoutStatus = new PoliPageException("config error").ToPayload();

        // Reflection-based serialization, as an integration's default options do; trimming and
        // AOT do not apply to test code.
#pragma warning disable IL2026, IL3050
        var full = JsonNode.Parse(JsonSerializer.Serialize(withRequestId, options))!.AsObject();
        var bare = JsonNode.Parse(JsonSerializer.Serialize(withoutStatus, options))!.AsObject();
#pragma warning restore IL2026, IL3050

        full.Select(p => p.Key).Should().Equal("code", "message", "status", "requestId");
        full["code"]!.GetValue<string>().Should().Be("INVALID_API_KEY");
        full["message"]!.GetValue<string>().Should().Be("Invalid API key");
        full["status"]!.GetValue<int>().Should().Be(401);
        full["requestId"]!.GetValue<string>().Should().Be("req_abc");
        bare.Select(p => p.Key).Should().Equal("code", "message", "status", "requestId");
        bare["status"].Should().BeNull();
        bare["requestId"].Should().BeNull();
    }

    [Fact]
    public void Gone_exception_convenience_constructors_default_to_DOCUMENT_GONE()
    {
        new PoliPageGoneException().Code.Should().Be(PoliPageErrorCode.DocumentGone);
        new PoliPageGoneException("m").Code.Should().Be(PoliPageErrorCode.DocumentGone);
        new PoliPageGoneException("m", new InvalidOperationException("inner")).Code.Should().Be(PoliPageErrorCode.DocumentGone);
    }
}
