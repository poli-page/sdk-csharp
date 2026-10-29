using System.Diagnostics;
using FluentAssertions;
using PoliPage.Tests.Contract;
using Xunit;
using static PoliPage.Tests.TransportTestSupport;

namespace PoliPage.Tests;

/// <summary>
/// <see cref="PoliPageClientOptions.RequestTimeout"/> bounds each attempt, connection,
/// headers and body together, whatever HttpClient is injected. The contract checks lower
/// bounds only; these check the upper bound.
/// </summary>
public sealed class AttemptTimeoutTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(200);

    // Far above the 200 ms timeout, far below the 100 s of HttpClient's default Timeout.
    private static readonly TimeSpan UpperBound = TimeSpan.FromSeconds(3);

    // Guards the test itself: a call the SDK does not bound must fail the test, not hang it.
    private static readonly TimeSpan TestGuard = TimeSpan.FromSeconds(15);

    public static TheoryData<string> Clients => new() { "sdk-owned", "injected" };

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task A_hung_attempt_fails_with_the_SDK_timeout_well_under_the_bound(string httpClient)
    {
        await using var server = new MockServer(_ => [MockStep.Hang]);
        // An injected client with no timeout of its own: only the SDK's can end the attempt.
        using var injected = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        var options = Options(server, timeout: Timeout) with
        {
            HttpClient = httpClient == "injected" ? injected : null,
        };
        using var client = new PoliPageClient(options);

        var watch = Stopwatch.StartNew();
        var act = () => client.Documents.GetAsync("doc_abc123").WaitAsync(TestGuard);

        var thrown = await act.Should().ThrowAsync<PoliPageException>();
        watch.Elapsed.Should().BeLessThan(UpperBound);
        thrown.Which.Code.Should().Be(PoliPageErrorCode.Timeout);
        thrown.Which.Message.Should().Be("Request timed out after 200ms");
        thrown.Which.IsRetryable().Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(Clients))]
    public async Task A_body_that_drips_past_the_timeout_fails_with_the_SDK_timeout(string httpClient)
    {
        // The headers come at once; the body, one byte every 50 ms, would take seconds.
        await using var server = new MockServer(origin => [Descriptor(origin).Drip()]);
        using var injected = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        var options = Options(server, timeout: Timeout) with
        {
            HttpClient = httpClient == "injected" ? injected : null,
        };
        using var client = new PoliPageClient(options);

        var watch = Stopwatch.StartNew();
        var act = () => client.Documents.GetAsync("doc_abc123").WaitAsync(TestGuard);

        var thrown = await act.Should().ThrowAsync<PoliPageException>();
        watch.Elapsed.Should().BeLessThan(UpperBound);
        thrown.Which.Code.Should().Be(PoliPageErrorCode.Timeout);
        thrown.Which.Message.Should().Be("Request timed out after 200ms");
    }

    [Fact]
    public async Task A_handler_that_ignores_cancellation_cannot_outlive_the_timeout()
    {
        await using var server = new MockServer(origin => [Descriptor(origin)]);
        using var injected = new HttpClient(new DeafHandler()) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        using var client = new PoliPageClient(Options(server, timeout: Timeout) with { HttpClient = injected });

        var watch = Stopwatch.StartNew();
        var act = () => client.Documents.GetAsync("doc_abc123").WaitAsync(TestGuard);

        (await act.Should().ThrowAsync<PoliPageException>()).Which.Message.Should().Be("Request timed out after 200ms");
        watch.Elapsed.Should().BeLessThan(UpperBound);
    }

    [Fact]
    public async Task A_shorter_timeout_of_the_injected_client_is_the_bound_the_error_names()
    {
        await using var server = new MockServer(_ => [MockStep.Hang]);
        using var injected = new HttpClient { Timeout = TimeSpan.FromMilliseconds(300) };
        using var client = new PoliPageClient(Options(server, timeout: TimeSpan.FromSeconds(10)) with { HttpClient = injected });

        var act = () => client.Documents.GetAsync("doc_abc123").WaitAsync(TestGuard);

        var thrown = await act.Should().ThrowAsync<PoliPageException>();
        thrown.Which.Code.Should().Be(PoliPageErrorCode.Timeout);
        thrown.Which.Message.Should().Be("Request timed out after 300ms");
    }

    [Fact]
    public async Task A_per_call_timeout_is_the_bound_the_error_names()
    {
        await using var server = new MockServer(_ => [MockStep.Hang]);
        using var client = new PoliPageClient(Options(server, timeout: TimeSpan.FromSeconds(10)));

        var act = () => client.Documents
            .GetAsync("doc_abc123", new RequestOptions { RequestTimeout = TimeSpan.FromMilliseconds(250) })
            .WaitAsync(TestGuard);

        (await act.Should().ThrowAsync<PoliPageException>()).Which.Message.Should().Be("Request timed out after 250ms");
    }

    [Fact]
    public void The_client_the_SDK_creates_has_no_timeout_of_its_own()
    {
        using var client = new PoliPageClient(new PoliPageClientOptions
        {
            ApiKey = ApiKey,
            RequestTimeout = TimeSpan.FromMinutes(3),
        });

        // HttpClient's default 100 s would cut a 3-minute RequestTimeout short.
        client.HttpClient.Timeout.Should().Be(System.Threading.Timeout.InfiniteTimeSpan);
    }

    /// <summary>A handler that never answers and ignores its cancellation token.</summary>
    private sealed class DeafHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }
}
