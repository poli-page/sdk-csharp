using System.Text;
using System.Text.Json;
using FluentAssertions;
using PoliPage.Tests.Contract;
using Xunit;
using static PoliPage.Tests.TransportTestSupport;

namespace PoliPage.Tests;

/// <summary>
/// A 2xx whose body is not JSON, from a call that expects JSON, is <c>invalid_response</c>:
/// the real status and request id, never retried.
/// </summary>
public sealed class InvalidResponseTests
{
    private static readonly byte[] Html = Encoding.UTF8.GetBytes("<html><body><h1>Welcome</h1></body></html>");

    private static MockStep HtmlAnswer(int status = 200)
        => new(status, [new("Content-Type", "text/html"), new("X-Request-Id", "req_portal")], Html, null);

    public static TheoryData<string> JsonCalls => new()
    {
        "render.document",
        "render.pdf",
        "render.pdfStream",
        "documents.get",
        "documents.thumbnails",
    };

    [Theory]
    [MemberData(nameof(JsonCalls))]
    public async Task A_2xx_html_body_is_invalid_response_and_is_not_retried(string call)
    {
        await using var server = new MockServer(_ => [HtmlAnswer(), HtmlAnswer(), HtmlAnswer()]);
        var errors = new List<Exception>();
        var retries = new List<RetryEvent>();
        using var client = new PoliPageClient(Options(server, maxRetries: 2) with
        {
            OnError = errors.Add,
            OnRetry = retries.Add,
        });

        var act = () => CallAsync(client, call);

        var thrown = await act.Should().ThrowAsync<PoliPageInvalidResponseException>();
        thrown.Which.Code.Should().Be(PoliPageErrorCode.InvalidResponse);
        thrown.Which.Message.Should().Be("Invalid response: HTTP 200 body is not JSON");
        thrown.Which.StatusCode.Should().Be(200);
        thrown.Which.RequestId.Should().Be("req_portal");
        thrown.Which.InnerException.Should().BeAssignableTo<JsonException>();
        thrown.Which.IsRetryable().Should().BeFalse();
        thrown.Which.IsNetworkError().Should().BeFalse();
        server.Requests.Should().ContainSingle("invalid_response is never retried");
        retries.Should().BeEmpty();
        errors.Should().ContainSingle().Which.Should().BeSameAs(thrown.Which);
    }

    [Theory]
    [InlineData(201, "")]
    [InlineData(200, "   ")]
    [InlineData(200, "{\"documentId\":")]
    [InlineData(200, "{} trailing")]
    [InlineData(200, "{'single':'quotes'}")]
    public async Task A_2xx_body_that_is_not_one_JSON_value_is_invalid_response(int status, string body)
    {
        var answer = new MockStep(status, [new("Content-Type", "application/json")], Encoding.UTF8.GetBytes(body), null);
        await using var server = new MockServer(_ => [answer]);
        using var client = new PoliPageClient(Options(server));

        var act = () => client.Documents.GetAsync("doc_abc123");

        var thrown = await act.Should().ThrowAsync<PoliPageInvalidResponseException>();
        thrown.Which.Message.Should().Be($"Invalid response: HTTP {status} body is not JSON");
        thrown.Which.StatusCode.Should().Be(status);
        thrown.Which.RequestId.Should().BeNull();
    }

    [Fact]
    public async Task A_JSON_body_with_a_UTF8_byte_order_mark_is_JSON()
    {
        await using var server = new MockServer(origin =>
        {
            var descriptor = Descriptor(origin);
            return [descriptor with { Body = [0xEF, 0xBB, 0xBF, .. descriptor.Body] }];
        });
        using var client = new PoliPageClient(Options(server));

        var document = await client.Documents.GetAsync("doc_abc123");

        document.DocumentId.Should().Be("doc_abc123");
    }

    [Fact]
    public async Task A_preview_is_HTML_not_JSON()
    {
        var preview = new MockStep(200, [new("Content-Type", "text/html"), new("X-Document-Page-Count", "2")], Html, null);
        await using var server = new MockServer(_ => [preview]);
        using var client = new PoliPageClient(Options(server));

        var result = await client.Documents.PreviewAsync("doc_abc123");

        result.Html.Should().Be(Encoding.UTF8.GetString(Html));
        result.PageCount.Should().Be(2);
    }

    [Fact]
    public async Task A_delete_ignores_a_body_that_is_not_JSON()
    {
        await using var server = new MockServer(_ => [HtmlAnswer()]);
        using var client = new PoliPageClient(Options(server));

        var act = () => client.Documents.DeleteAsync("doc_abc123");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Its_payload_answers_502_and_the_error_keeps_its_2xx()
    {
        await using var server = new MockServer(_ => [HtmlAnswer()]);
        using var client = new PoliPageClient(Options(server));

        var act = () => client.Documents.GetAsync("doc_abc123");

        var thrown = (await act.Should().ThrowAsync<PoliPageInvalidResponseException>()).Which;
        thrown.ToPayload().Should().Be(new PoliPageErrorPayload(
            PoliPageErrorCode.InvalidResponse, "Invalid response: HTTP 200 body is not JSON", 502, "req_portal"));
        thrown.StatusCode.Should().Be(200);
    }

    private static async Task CallAsync(PoliPageClient client, string call)
    {
        switch (call)
        {
            case "render.document":
                await client.Render.DocumentAsync(Input);
                break;
            case "render.pdf":
                await client.Render.PdfAsync(Input);
                break;
            case "render.pdfStream":
                await (await client.Render.PdfStreamAsync(Input)).DisposeAsync();
                break;
            case "documents.get":
                await client.Documents.GetAsync("doc_abc123");
                break;
            case "documents.thumbnails":
                await client.Documents.ThumbnailsAsync("doc_abc123");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(call), call, "unknown call");
        }
    }
}
