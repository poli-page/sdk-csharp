using FluentAssertions;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;
using Xunit;

namespace PoliPage.Tests;

public sealed class HookEventsTests
{
    private const string SampleDescriptorJson = """
        {
            "documentId": "doc_abc",
            "organizationId": "org_x",
            "environment": "sandbox",
            "format": "pdf",
            "pageCount": 1,
            "sizeBytes": 100,
            "createdAt": "2026-06-03T10:00:00Z",
            "presignedPdfUrl": "https://placeholder.invalid/doc.pdf",
            "expiresAt": "2026-06-03T10:15:00Z"
        }
        """;

    private static RenderInput Input() => new()
    {
        Project = "billing",
        Template = "invoice",
        Data = new { customer = "Acme" },
    };

    [Fact]
    public async Task OnRequest_fires_once_per_attempt_with_method_url_and_attempt_number()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(Response.Create().WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(SampleDescriptorJson));

        var events = new List<RequestEvent>();
        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test",
            BaseUrl = new Uri(server.Url!),
            MaxRetries = 0,
            OnRequest = e => events.Add(e),
        });

        await client.Render.DocumentAsync(Input());

        events.Should().ContainSingle();
        events[0].Method.Should().Be("POST");
        events[0].Url.Should().EndWith("/v1/render");
        events[0].Attempt.Should().Be(1);
    }

    [Fact]
    public async Task OnResponse_fires_on_success_with_status_requestId_and_durationMs()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(Response.Create().WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithHeader("X-Request-Id", "req_xyz")
                  .WithBody(SampleDescriptorJson));

        var events = new List<ResponseEvent>();
        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test",
            BaseUrl = new Uri(server.Url!),
            MaxRetries = 0,
            OnResponse = e => events.Add(e),
        });

        await client.Render.DocumentAsync(Input());

        events.Should().ContainSingle();
        events[0].Status.Should().Be(200);
        events[0].RequestId.Should().Be("req_xyz");
        events[0].DurationMs.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task OnRequest_attempt_increments_across_retries()
    {
        using var server = WireMockServer.Start();
        // First two responses 500, third 200.
        var scenario = "retry";
        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .InScenario(scenario).WillSetStateTo("attempt2")
              .RespondWith(Response.Create().WithStatusCode(500).WithBody("{}"));
        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .InScenario(scenario).WhenStateIs("attempt2").WillSetStateTo("attempt3")
              .RespondWith(Response.Create().WithStatusCode(500).WithBody("{}"));
        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .InScenario(scenario).WhenStateIs("attempt3")
              .RespondWith(Response.Create().WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(SampleDescriptorJson));

        var attempts = new List<int>();
        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test",
            BaseUrl = new Uri(server.Url!),
            MaxRetries = 2,
            RetryDelay = TimeSpan.FromMilliseconds(1),
            OnRequest = e => attempts.Add(e.Attempt),
        });

        await client.Render.DocumentAsync(Input());

        attempts.Should().Equal(1, 2, 3);
    }

    [Fact]
    public async Task OnRequest_exception_does_not_break_the_request()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(Response.Create().WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(SampleDescriptorJson));

        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test",
            BaseUrl = new Uri(server.Url!),
            MaxRetries = 0,
            OnRequest = _ => throw new InvalidOperationException("boom"),
        });

        var act = async () => await client.Render.DocumentAsync(Input());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task OnResponse_exception_does_not_break_the_request()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(Response.Create().WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(SampleDescriptorJson));

        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test",
            BaseUrl = new Uri(server.Url!),
            MaxRetries = 0,
            OnResponse = _ => throw new InvalidOperationException("boom"),
        });

        var act = async () => await client.Render.DocumentAsync(Input());

        await act.Should().NotThrowAsync();
    }

    // ── Task 11: OnError tests ───────────────────────────────────────────────

    [Fact]
    public async Task OnError_fires_with_PoliPageException_on_4xx_response()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/v1/documents/doc_missing").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(404)
                  .WithBody("""{"error":"NOT_FOUND","detail":"missing"}"""));

        var errors = new List<Exception>();
        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test",
            BaseUrl = new Uri(server.Url!),
            MaxRetries = 0,
            OnError = e => errors.Add(e),
        });

        var act = async () => await client.Documents.GetAsync("doc_missing");

        await act.Should().ThrowAsync<PoliPageNotFoundException>();
        errors.Should().ContainSingle().Which.Should().BeOfType<PoliPageNotFoundException>();
    }

    [Fact]
    public async Task OnError_fires_with_network_exception_on_connection_failure()
    {
        // Point at a port that is not listening so HttpRequestException is thrown.
        var unreachableUrl = new Uri("http://127.0.0.1:1");

        var errors = new List<Exception>();
        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test",
            BaseUrl = unreachableUrl,
            MaxRetries = 0,
            OnError = e => errors.Add(e),
        });

        var act = async () => await client.Documents.GetAsync("doc_x");

        await act.Should().ThrowAsync<PoliPageNetworkException>();
        errors.Should().ContainSingle().Which.Should().BeOfType<PoliPageNetworkException>();
    }

    [Fact]
    public async Task OnError_fires_once_with_aborted_on_caller_cancellation()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(Response.Create().WithStatusCode(200)
                  .WithHeader("Content-Type", "application/json")
                  .WithBody(SampleDescriptorJson));

        var errors = new List<Exception>();
        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test",
            BaseUrl = new Uri(server.Url!),
            MaxRetries = 0,
            OnError = errors.Add,
        });

        // A pre-cancelled token: the SDK raises `aborted` (spec §8.3) and OnError sees it.
        using var cts = new System.Threading.CancellationTokenSource();
        cts.Cancel();

        var act = async () => await client.Render.DocumentAsync(Input(), cancellationToken: cts.Token);

        var thrown = await act.Should().ThrowAsync<PoliPageException>();
        thrown.Which.Code.Should().Be(PoliPageErrorCode.Aborted);
        errors.Should().ContainSingle().Which.Should().BeSameAs(thrown.Which);
    }

    [Fact]
    public async Task OnError_throwing_callback_does_not_break_exception_propagation()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/v1/documents/doc_x").UsingGet())
              .RespondWith(Response.Create().WithStatusCode(404)
                  .WithBody("""{"error":"NOT_FOUND","detail":"missing"}"""));

        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test",
            BaseUrl = new Uri(server.Url!),
            MaxRetries = 0,
            OnError = _ => throw new InvalidOperationException("hook blew up"),
        });

        var act = async () => await client.Documents.GetAsync("doc_x");

        // The original PoliPageNotFoundException must surface, not the hook's InvalidOperationException.
        await act.Should().ThrowAsync<PoliPageNotFoundException>();
    }

    [Fact]
    public async Task OnError_fires_once_after_retries_exhausted()
    {
        using var server = WireMockServer.Start();
        server.Given(Request.Create().WithPath("/v1/render").UsingPost())
              .RespondWith(Response.Create().WithStatusCode(500)
                  .WithBody("""{"error":"INTERNAL_ERROR","detail":"oops"}"""));

        var errors = new List<Exception>();
        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = "pp_test",
            BaseUrl = new Uri(server.Url!),
            MaxRetries = 1,
            RetryDelay = TimeSpan.FromMilliseconds(1),
            OnError = e => errors.Add(e),
        });

        var act = async () => await client.Render.DocumentAsync(Input());

        await act.Should().ThrowAsync<PoliPageException>();
        errors.Should().ContainSingle("OnError fires exactly once after retries are exhausted");
    }
}
