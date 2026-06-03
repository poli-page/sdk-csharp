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
            PoliPageErrorCode.Network, "dns failure", new HttpRequestException("dns"));

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
}
