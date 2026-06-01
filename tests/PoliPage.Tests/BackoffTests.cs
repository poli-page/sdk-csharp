using FluentAssertions;
using PoliPage.Internal;
using Xunit;

namespace PoliPage.Tests;

/// <summary>
/// Unit tests for <see cref="Backoff.ComputeDelay"/> — pure function, no I/O.
/// </summary>
public sealed class BackoffTests
{
    // ------------------------------------------------------------------ //
    // Jitter = 1.0 throughout: deterministic results
    // ------------------------------------------------------------------ //

    private const double NoJitter = 1.0;

    // ------------------------------------------------------------------ //
    // 1. Attempt 1 with base 100ms → 100ms (2^0 = 1)
    // ------------------------------------------------------------------ //

    [Fact]
    public void ComputeDelay_attempt1_base100ms_no_jitter_returns_100ms()
    {
        var delay = Backoff.ComputeDelay(1, TimeSpan.FromMilliseconds(100), retryAfter: null, jitterFactor: NoJitter);

        delay.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    // ------------------------------------------------------------------ //
    // 2. Attempt 2 with base 100ms → 200ms (2^1 = 2)
    // ------------------------------------------------------------------ //

    [Fact]
    public void ComputeDelay_attempt2_base100ms_no_jitter_returns_200ms()
    {
        var delay = Backoff.ComputeDelay(2, TimeSpan.FromMilliseconds(100), retryAfter: null, jitterFactor: NoJitter);

        delay.Should().Be(TimeSpan.FromMilliseconds(200));
    }

    // ------------------------------------------------------------------ //
    // 3. Attempt 3 with base 100ms → 400ms (2^2 = 4)
    // ------------------------------------------------------------------ //

    [Fact]
    public void ComputeDelay_attempt3_base100ms_no_jitter_returns_400ms()
    {
        var delay = Backoff.ComputeDelay(3, TimeSpan.FromMilliseconds(100), retryAfter: null, jitterFactor: NoJitter);

        delay.Should().Be(TimeSpan.FromMilliseconds(400));
    }

    // ------------------------------------------------------------------ //
    // 4. Large attempt number caps at 30 s
    // ------------------------------------------------------------------ //

    [Fact]
    public void ComputeDelay_large_attempt_caps_at_MaxDelay()
    {
        var delay = Backoff.ComputeDelay(100, TimeSpan.FromMilliseconds(500), retryAfter: null, jitterFactor: NoJitter);

        delay.Should().Be(Backoff.MaxDelay);
    }

    // ------------------------------------------------------------------ //
    // 5. Retry-After override: server value is used instead of exponential delay
    // ------------------------------------------------------------------ //

    [Fact]
    public void ComputeDelay_retryAfter_overrides_exponential()
    {
        var retryAfter = TimeSpan.FromSeconds(5);
        var delay = Backoff.ComputeDelay(1, TimeSpan.FromMilliseconds(100), retryAfter, jitterFactor: NoJitter);

        delay.Should().Be(TimeSpan.FromSeconds(5));
    }

    // ------------------------------------------------------------------ //
    // 6. Retry-After > 30 s is capped at MaxDelay
    // ------------------------------------------------------------------ //

    [Fact]
    public void ComputeDelay_retryAfter_exceeding_MaxDelay_is_capped()
    {
        var retryAfter = TimeSpan.FromSeconds(120);
        var delay = Backoff.ComputeDelay(1, TimeSpan.FromMilliseconds(500), retryAfter, jitterFactor: NoJitter);

        delay.Should().Be(Backoff.MaxDelay);
    }

    // ------------------------------------------------------------------ //
    // 7. Retry-After == 30 s is not capped
    // ------------------------------------------------------------------ //

    [Fact]
    public void ComputeDelay_retryAfter_exactly_MaxDelay_is_not_capped()
    {
        var retryAfter = TimeSpan.FromSeconds(30);
        var delay = Backoff.ComputeDelay(1, TimeSpan.FromMilliseconds(500), retryAfter, jitterFactor: NoJitter);

        delay.Should().Be(TimeSpan.FromSeconds(30));
    }

    // ------------------------------------------------------------------ //
    // 8. Retry-After = zero seconds is returned as zero
    // ------------------------------------------------------------------ //

    [Fact]
    public void ComputeDelay_retryAfter_zero_returns_zero()
    {
        var delay = Backoff.ComputeDelay(1, TimeSpan.FromMilliseconds(500), TimeSpan.Zero, jitterFactor: NoJitter);

        delay.Should().Be(TimeSpan.Zero);
    }

    // ------------------------------------------------------------------ //
    // 9. Negative Retry-After is clamped to zero
    // ------------------------------------------------------------------ //

    [Fact]
    public void ComputeDelay_negative_retryAfter_is_clamped_to_zero()
    {
        var delay = Backoff.ComputeDelay(1, TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(-5), jitterFactor: NoJitter);

        delay.Should().Be(TimeSpan.Zero);
    }

    // ------------------------------------------------------------------ //
    // 10. Jitter factor 0.5 scales the delay
    // ------------------------------------------------------------------ //

    [Fact]
    public void ComputeDelay_jitter_half_scales_delay()
    {
        var delay = Backoff.ComputeDelay(1, TimeSpan.FromMilliseconds(200), retryAfter: null, jitterFactor: 0.5);

        delay.Should().Be(TimeSpan.FromMilliseconds(100));
    }

    // ------------------------------------------------------------------ //
    // 11. Jitter factor 1.5 scales the delay upward
    // ------------------------------------------------------------------ //

    [Fact]
    public void ComputeDelay_jitter_1point5_scales_delay_upward()
    {
        var delay = Backoff.ComputeDelay(1, TimeSpan.FromMilliseconds(200), retryAfter: null, jitterFactor: 1.5);

        delay.Should().Be(TimeSpan.FromMilliseconds(300));
    }

    // ------------------------------------------------------------------ //
    // 12. MaxDelay constant is 30 seconds
    // ------------------------------------------------------------------ //

    [Fact]
    public void MaxDelay_is_30_seconds()
    {
        Backoff.MaxDelay.Should().Be(TimeSpan.FromSeconds(30));
    }
}
