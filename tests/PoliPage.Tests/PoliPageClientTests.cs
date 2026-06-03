using FluentAssertions;
using Xunit;

namespace PoliPage.Tests;

public sealed class PoliPageClientTests
{
    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private static PoliPageClientOptions ValidOptions(string apiKey = "pp_test_abc123") =>
        new() { ApiKey = apiKey };

    // ------------------------------------------------------------------ //
    // Constructor guard clauses
    // ------------------------------------------------------------------ //

    [Fact]
    public void Ctor_throws_ArgumentNullException_when_options_is_null()
    {
        Action act = () => _ = new PoliPageClient(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("options");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_throws_PoliPageException_with_invalid_options_when_ApiKey_is_null_empty_or_whitespace(string? apiKey)
    {
        var opts = new PoliPageClientOptions { ApiKey = apiKey! };

        Action act = () => _ = new PoliPageClient(opts);

        act.Should().Throw<PoliPageException>()
            .Where(e => e.Code == PoliPageErrorCode.InvalidOptions);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Ctor_throws_PoliPageException_with_invalid_options_when_MaxRetries_is_negative(int maxRetries)
    {
        var opts = ValidOptions() with { MaxRetries = maxRetries };

        Action act = () => _ = new PoliPageClient(opts);

        act.Should().Throw<PoliPageException>()
            .Where(e => e.Code == PoliPageErrorCode.InvalidOptions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ctor_throws_PoliPageException_with_invalid_options_when_RetryDelay_is_zero_or_negative(int milliseconds)
    {
        var opts = ValidOptions() with { RetryDelay = TimeSpan.FromMilliseconds(milliseconds) };

        Action act = () => _ = new PoliPageClient(opts);

        act.Should().Throw<PoliPageException>()
            .Where(e => e.Code == PoliPageErrorCode.InvalidOptions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ctor_throws_PoliPageException_with_invalid_options_when_RequestTimeout_is_zero_or_negative(int seconds)
    {
        var opts = ValidOptions() with { RequestTimeout = TimeSpan.FromSeconds(seconds) };

        Action act = () => _ = new PoliPageClient(opts);

        act.Should().Throw<PoliPageException>()
            .Where(e => e.Code == PoliPageErrorCode.InvalidOptions);
    }

    // ------------------------------------------------------------------ //
    // Constructor happy-path
    // ------------------------------------------------------------------ //

    [Fact]
    public void Ctor_uses_default_BaseAddress_when_BaseUrl_not_provided()
    {
        using var client = new PoliPageClient(ValidOptions());

        client.BaseAddress.Should().Be(new Uri("https://api.poli.page"));
    }

    [Fact]
    public void Ctor_uses_provided_BaseUrl_when_set()
    {
        var custom = new Uri("https://dev.poli.page");
        using var client = new PoliPageClient(ValidOptions() with { BaseUrl = custom });

        client.BaseAddress.Should().Be(custom);
    }

    [Fact]
    public void Ctor_accepts_minimum_valid_options()
    {
        var act = () =>
        {
            using var client = new PoliPageClient(new PoliPageClientOptions { ApiKey = "pp_test_min" });
        };

        act.Should().NotThrow();
    }

    // ------------------------------------------------------------------ //
    // Dispose semantics
    // ------------------------------------------------------------------ //

    [Fact]
    public async Task Dispose_disposes_owned_HttpClient()
    {
        var client = new PoliPageClient(ValidOptions());
        var owned = client.HttpClient;

        client.Dispose();

        client.IsDisposed.Should().BeTrue();

        // Confirm the underlying HttpClient is itself disposed, not just the wrapper flag.
        // Disposed HttpClient throws on SendAsync (either ObjectDisposedException directly
        // or wrapped in InvalidOperationException depending on the runtime).
        var send = async () => await owned.SendAsync(new HttpRequestMessage(HttpMethod.Get, "https://x"));
        await send.Should().ThrowAsync<Exception>()
            .Where(e => e is ObjectDisposedException || e is InvalidOperationException);
    }

    [Fact]
    public void Dispose_is_idempotent()
    {
        var client = new PoliPageClient(ValidOptions());

        var act = () =>
        {
            client.Dispose();
            client.Dispose();
        };

        act.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_does_not_dispose_caller_provided_HttpClient()
    {
        var callerOwned = new HttpClient { BaseAddress = new Uri("https://caller.example.com") };
        var client = new PoliPageClient(ValidOptions() with { HttpClient = callerOwned });

        client.Dispose();

        client.IsDisposed.Should().BeTrue();

        // The caller-owned HttpClient must still be operational. Use a pre-cancelled
        // token: a disposed HttpClient would throw ObjectDisposedException, a healthy
        // one throws OperationCanceledException because cancellation wins before I/O
        // begins. Asserting on a property like BaseAddress would be a false-positive —
        // properties on disposed HttpClient instances do not throw.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var send = async () => await callerOwned.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, "https://caller.example.com/probe"),
            cts.Token);
        await send.Should().ThrowAsync<OperationCanceledException>();

        callerOwned.Dispose();
    }
}
