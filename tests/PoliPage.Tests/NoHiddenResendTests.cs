using FluentAssertions;
using PoliPage.Tests.Contract;
using Xunit;
using static PoliPage.Tests.TransportTestSupport;

namespace PoliPage.Tests;

/// <summary>
/// SocketsHttpHandler sends a request again by itself, up to three more times, when its
/// connection closes before any byte of the answer, if the request has no content or waits
/// for <c>100 Continue</c>. Every resend must be the SDK's own, counted retry: with
/// <c>MaxRetries = 0</c> a dropped request is sent exactly once.
/// </summary>
/// <remarks>
/// Each test answers a first call on a keep-alive connection, then drops the second
/// request, which the client sends on that same connection: the
/// <see cref="RecordedRequest.Connection"/> check is the control that the connection
/// really was reused.
/// </remarks>
public sealed class NoHiddenResendTests
{
    [Fact]
    public async Task Get_on_a_reused_connection_that_dies_is_sent_once()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), MockStep.Drop]);
        using var client = new PoliPageClient(Options(server));

        await client.Documents.GetAsync("doc_abc123");
        var act = () => client.Documents.GetAsync("doc_abc123");

        (await act.Should().ThrowAsync<PoliPageNetworkException>()).Which.Code.Should().Be(PoliPageErrorCode.NetworkError);
        AssertSentOnceOnTheReusedConnection(server);
    }

    [Fact]
    public async Task Delete_on_a_reused_connection_that_dies_is_sent_once()
    {
        await using var server = new MockServer(_ => [NoContent(), MockStep.Drop]);
        using var client = new PoliPageClient(Options(server));

        await client.Documents.DeleteAsync("doc_abc123");
        var act = () => client.Documents.DeleteAsync("doc_abc123");

        (await act.Should().ThrowAsync<PoliPageException>()).Which.Code.Should().Be(PoliPageErrorCode.NetworkError);
        AssertSentOnceOnTheReusedConnection(server);
    }

    [Fact]
    public async Task Post_on_a_reused_connection_that_dies_is_sent_once()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), MockStep.Drop]);
        using var client = new PoliPageClient(Options(server));

        await client.Render.DocumentAsync(Input);
        var act = () => client.Render.DocumentAsync(Input);

        (await act.Should().ThrowAsync<PoliPageException>()).Which.Code.Should().Be(PoliPageErrorCode.NetworkError);
        AssertSentOnceOnTheReusedConnection(server);
    }

    [Fact]
    public async Task Post_whose_body_was_sent_is_sent_once_even_through_a_client_asking_for_100_continue()
    {
        await using var server = new MockServer(origin => [Descriptor(origin), MockStep.Drop]);
        using var http = new HttpClient();
        http.DefaultRequestHeaders.ExpectContinue = true;
        using var client = new PoliPageClient(Options(server) with { HttpClient = http });

        await client.Render.DocumentAsync(Input);
        var act = () => client.Render.DocumentAsync(Input);

        (await act.Should().ThrowAsync<PoliPageException>()).Which.Code.Should().Be(PoliPageErrorCode.NetworkError);
        // The client waits for 100 Continue, gets none, and sends the body: once the body
        // has started, the handler never resends.
        AssertSentOnceOnTheReusedConnection(server);
    }

    [Fact]
    public async Task Download_on_the_reused_api_connection_that_dies_is_sent_once()
    {
        // One injected HttpClient for the API and the download, so the download goes out on
        // the render's keep-alive connection.
        await using var server = new MockServer(origin => [Descriptor(origin), MockStep.Drop]);
        using var http = new HttpClient();
        using var client = new PoliPageClient(Options(server) with { HttpClient = http, DownloadHttpClient = http });

        var act = () => client.Render.PdfAsync(Input);

        var thrown = await act.Should().ThrowAsync<PoliPageDownloadException>();
        thrown.Which.Code.Should().Be(PoliPageErrorCode.DownloadFailed);
        thrown.Which.Message.Should().Be("Failed to download PDF: network error");
        thrown.Which.StatusCode.Should().Be(0);
        thrown.Which.InnerException.Should().BeOfType<HttpRequestException>();
        AssertSentOnceOnTheReusedConnection(server);
    }

    [Fact]
    public async Task Every_request_stays_on_HTTP_1_1_even_through_a_client_asking_for_HTTP_2()
    {
        // Over HTTP/2, SocketsHttpHandler resends a refused (REFUSED_STREAM) or unprocessed
        // (GOAWAY) request by itself, whatever its content: the SDK never asks for HTTP/2.
        // A guard, not a control: HttpClient.DefaultRequestVersion never applied to a request
        // the SDK builds itself, so this also passes without the explicit pin.
        await using var server = new MockServer(origin => [Descriptor(origin), Bytes(200, Pdf, "application/pdf")]);
        var seen = new List<(Version Version, HttpVersionPolicy Policy)>();
        using var http = new HttpClient(new CapturingHandler(seen, new SocketsHttpHandler()))
        {
            DefaultRequestVersion = System.Net.HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrHigher,
        };
        using var client = new PoliPageClient(Options(server) with { HttpClient = http, DownloadHttpClient = http });

        await client.Render.PdfAsync(Input);

        seen.Should().HaveCount(2).And.OnlyContain(
            s => s.Version == System.Net.HttpVersion.Version11 && s.Policy == HttpVersionPolicy.RequestVersionOrLower);
    }

    private static void AssertSentOnceOnTheReusedConnection(MockServer server)
    {
        var requests = server.Requests;
        requests.Should().HaveCount(2, "the dropped request must not be sent again behind the SDK's back");
        requests[1].Connection.Should().Be(requests[0].Connection, "the control: the second request reused the connection");
    }

    private sealed class CapturingHandler(List<(Version Version, HttpVersionPolicy Policy)> seen, HttpMessageHandler inner)
        : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (seen)
                seen.Add((request.Version, request.VersionPolicy));
            return base.SendAsync(request, cancellationToken);
        }
    }
}
