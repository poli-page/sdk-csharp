using System.Diagnostics;
using FluentAssertions;
using PoliPage.Internal;
using Xunit;

namespace PoliPage.Tests;

/// <summary>
/// R-WAIT-FULL: a timeout fires no sooner than its bound after it started. Timers built on
/// the coarse <c>Environment.TickCount64</c> (<c>CancellationTokenSource(timeout)</c>,
/// <c>Task.Delay</c>) can wake up to a tick early, so <see cref="FullTimeout"/> waits again.
/// </summary>
public sealed class FullTimeoutTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromMilliseconds(300);

    [Fact]
    public async Task A_timer_that_wakes_early_waits_again_for_the_rest()
    {
        // The first wait returns at once, as a timer that wakes early says it is done; later
        // waits take what they are asked. Without the re-arm the token fires at about 0 ms,
        // whatever the runner's load, so the test never depends on scheduling.
        var wakes = 0;
        Task EarlyDelay(TimeSpan asked, CancellationToken token)
        {
            wakes++;
            return wakes == 1 ? Task.CompletedTask : Task.Delay(asked, token);
        }

        var watch = Stopwatch.StartNew();
        using var timeout = new FullTimeout(Bound, EarlyDelay);
        await WaitUntilCancelledAsync(timeout.Token);
        watch.Stop();

        watch.Elapsed.Should().BeGreaterThanOrEqualTo(Bound);
        wakes.Should().BeGreaterThan(1);
    }

    [Fact]
    public async Task A_timer_fires_after_its_bound()
    {
        var watch = Stopwatch.StartNew();
        using var timeout = new FullTimeout(Bound);
        await WaitUntilCancelledAsync(timeout.Token);
        watch.Stop();

        timeout.IsCancellationRequested.Should().BeTrue();
        watch.Elapsed.Should().BeGreaterThanOrEqualTo(Bound);
    }

    [Fact]
    public async Task A_disposed_timer_never_fires()
    {
        var timeout = new FullTimeout(TimeSpan.FromMilliseconds(100));
        var token = timeout.Token;
        timeout.Dispose();

        await Task.Delay(300);

        token.IsCancellationRequested.Should().BeFalse();
    }

    [Fact]
    public async Task An_infinite_timer_never_fires()
    {
        using var timeout = new FullTimeout(System.Threading.Timeout.InfiniteTimeSpan);

        await Task.Delay(100);

        timeout.IsCancellationRequested.Should().BeFalse();
    }

    [Theory]
    [InlineData(60)] // above Task.Delay's longest wait (about 49.7 days)
    [InlineData(-1)] // TimeSpan.MaxValue: past the monotonic clock's range
    public async Task A_timer_longer_than_one_delay_waits_in_capped_steps(int days)
    {
        var bound = days < 0 ? TimeSpan.MaxValue : TimeSpan.FromDays(days);
        var asked = new List<TimeSpan>();
        Exception? thrown = null;
        Task RealDelay(TimeSpan delay, CancellationToken token)
        {
            lock (asked)
                asked.Add(delay);
            try
            {
                return Task.Delay(delay, token);
            }
            catch (Exception ex)
            {
                thrown = ex;
                throw;
            }
        }

        using var timeout = new FullTimeout(bound, RealDelay);
        await Task.Delay(100);

        thrown.Should().BeNull();
        timeout.IsCancellationRequested.Should().BeFalse();
        lock (asked)
            asked.Should().Equal(MonotonicWait.MaxSingleDelay);
    }

    [Fact]
    public void A_negative_timer_is_rejected_as_by_a_CancellationTokenSource()
    {
        var act = () => new FullTimeout(TimeSpan.FromMilliseconds(-2));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_deadline_saturates_instead_of_overflowing()
    {
        var oneSecond = Stopwatch.Frequency;

        MonotonicWait.Deadline(1_000, TimeSpan.FromSeconds(1)).Should().Be(1_000 + oneSecond);
        MonotonicWait.Deadline(1_000, TimeSpan.MaxValue).Should().Be(long.MaxValue);
        MonotonicWait.Deadline(long.MaxValue - 10, TimeSpan.FromSeconds(1)).Should().Be(long.MaxValue);
        MonotonicWait.Deadline(1_000, TimeSpan.Zero).Should().Be(1_000);
    }

    [Fact]
    public void A_single_delay_is_rounded_up_and_capped_at_the_TaskDelay_maximum()
    {
        var oneMs = Stopwatch.Frequency / 1000.0;
        const long now = 1_000;

        MonotonicWait.NextDelay(now, now).Should().BeNull();
        MonotonicWait.NextDelay(now - 1, now).Should().BeNull();
        MonotonicWait.NextDelay(now + (long)Math.Ceiling(1.5 * oneMs), now).Should().Be(TimeSpan.FromMilliseconds(2));
        MonotonicWait.NextDelay(MonotonicWait.Deadline(now, TimeSpan.FromDays(60)), now)
            .Should().Be(MonotonicWait.MaxSingleDelay);
        MonotonicWait.NextDelay(long.MaxValue, now).Should().Be(MonotonicWait.MaxSingleDelay);

        // The cap is the longest wait Task.Delay accepts.
        var cancelled = new CancellationToken(canceled: true);
        Action longest = () => _ = Task.Delay(MonotonicWait.MaxSingleDelay, cancelled);
        Action longer = () => _ = Task.Delay(MonotonicWait.MaxSingleDelay + TimeSpan.FromMilliseconds(1), cancelled);
        longest.Should().NotThrow();
        longer.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static async Task WaitUntilCancelledAsync(CancellationToken token)
    {
        var done = new TaskCompletionSource();
        await using var _ = token.Register(() => done.SetResult());
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
    }
}
