using System.Diagnostics;
using System.Globalization;
using FluentAssertions;
using PoliPage.Internal;
using Xunit;

namespace PoliPage.Tests;

/// <summary>
/// <c>Retry-After</c> (spec §9.2): integer seconds or an HTTP-date, clamped to 0–30 s;
/// anything else falls back to the backoff.
/// </summary>
public sealed class RetryAfterParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 7, 28, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData(" 7 ", 7)]
    [InlineData("0005", 5)]
    [InlineData("29", 29)]
    [InlineData("30", 30)]
    [InlineData("31", 30)]
    [InlineData("120", 30)]
    [InlineData("3600", 30)]
    // Too large for any integer type: clamps to the cap, never overflows.
    [InlineData("99999999999999999999999999999999", 30)]
    public void Integer_seconds_set_the_delay_clamped_to_30_s(string header, int seconds)
        => RetryAfterParser.Parse(header, Now).Should().Be(TimeSpan.FromSeconds(seconds));

    [Fact]
    public void An_http_date_sets_the_time_left_until_it()
        => RetryAfterParser.Parse(HttpDate(Now.AddSeconds(2)), Now).Should().Be(TimeSpan.FromSeconds(2));

    [Fact]
    public void An_http_date_far_ahead_clamps_to_30_s()
        => RetryAfterParser.Parse(HttpDate(Now.AddHours(1)), Now).Should().Be(TimeSpan.FromSeconds(30));

    [Fact]
    public void An_http_date_in_the_past_gives_zero()
        => RetryAfterParser.Parse(HttpDate(Now.AddSeconds(-30)), Now).Should().Be(TimeSpan.Zero);

    [Theory]
    [InlineData("")]
    [InlineData("soon")]
    [InlineData("-1")]
    [InlineData("+5")]
    [InlineData("1.5")]
    [InlineData("tomorrow")]
    public void Anything_else_falls_back_to_the_backoff(string header)
        => RetryAfterParser.Parse(header, Now).Should().BeNull();

    [Fact]
    public async Task A_retry_wait_is_never_shorter_than_its_delay()
    {
        // Task.Delay's timer can fire before the delay has passed on the monotonic clock;
        // the SDK waits again for the rest.
        for (var i = 0; i < 20; i++)
        {
            var delay = TimeSpan.FromMilliseconds(5 + i);
            var start = Stopwatch.GetTimestamp();
            await HttpTransport.SleepAsync(delay, CancellationToken.None);
            Stopwatch.GetElapsedTime(start).Should().BeGreaterThanOrEqualTo(delay);
        }
    }

    [Fact]
    public async Task A_cancellation_during_a_retry_wait_raises_aborted_at_once()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var start = Stopwatch.GetTimestamp();

        var act = () => HttpTransport.SleepAsync(TimeSpan.FromSeconds(10), cts.Token);

        var thrown = await act.Should().ThrowAsync<PoliPageException>();
        thrown.Which.Code.Should().Be(PoliPageErrorCode.Aborted);
        Stopwatch.GetElapsedTime(start).Should().BeLessThan(TimeSpan.FromSeconds(3));
    }

    private static string HttpDate(DateTimeOffset at) => at.ToString("r", CultureInfo.InvariantCulture);
}
