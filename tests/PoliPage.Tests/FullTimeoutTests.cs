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
        // A timer that always wakes after half of what it was asked for, and says it is done.
        var wakes = 0;
        Task EarlyDelay(TimeSpan asked, CancellationToken token)
        {
            wakes++;
            return Task.Delay(asked / 2, token);
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

    private static async Task WaitUntilCancelledAsync(CancellationToken token)
    {
        var done = new TaskCompletionSource();
        await using var _ = token.Register(() => done.SetResult());
        await done.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None);
    }
}
