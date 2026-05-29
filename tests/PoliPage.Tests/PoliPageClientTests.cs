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
    public void Ctor_throws_ArgumentException_when_ApiKey_is_null_empty_or_whitespace(string? apiKey)
    {
        var opts = new PoliPageClientOptions { ApiKey = apiKey! };

        Action act = () => _ = new PoliPageClient(opts);

        act.Should().Throw<ArgumentException>()
            .WithParameterName("options");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Ctor_throws_ArgumentOutOfRangeException_when_MaxRetries_is_negative(int maxRetries)
    {
        var opts = ValidOptions() with { MaxRetries = maxRetries };

        Action act = () => _ = new PoliPageClient(opts);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("options");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ctor_throws_ArgumentOutOfRangeException_when_RetryDelay_is_zero_or_negative(int milliseconds)
    {
        var opts = ValidOptions() with { RetryDelay = TimeSpan.FromMilliseconds(milliseconds) };

        Action act = () => _ = new PoliPageClient(opts);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("options");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Ctor_throws_ArgumentOutOfRangeException_when_RequestTimeout_is_zero_or_negative(int seconds)
    {
        var opts = ValidOptions() with { RequestTimeout = TimeSpan.FromSeconds(seconds) };

        Action act = () => _ = new PoliPageClient(opts);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("options");
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
    public void Dispose_disposes_owned_HttpClient()
    {
        var client = new PoliPageClient(ValidOptions());

        client.Dispose();

        client.IsDisposed.Should().BeTrue();
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
    public void Dispose_does_not_dispose_caller_provided_HttpClient()
    {
        var callerOwned = new HttpClient { BaseAddress = new Uri("https://caller.example.com") };
        var client = new PoliPageClient(ValidOptions() with { HttpClient = callerOwned });

        client.Dispose();

        // The caller-owned HttpClient must still be usable (reading BaseAddress is sufficient).
        var act = () => _ = callerOwned.BaseAddress;
        act.Should().NotThrow();

        // The PoliPageClient itself is disposed.
        client.IsDisposed.Should().BeTrue();

        callerOwned.Dispose();
    }
}
