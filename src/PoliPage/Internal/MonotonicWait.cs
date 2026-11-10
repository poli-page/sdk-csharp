using System.Diagnostics;

namespace PoliPage.Internal;

/// <summary>
/// Waits until a deadline on the monotonic clock (<see cref="Stopwatch"/>), never less
/// (<c>R-WAIT-FULL</c>). Shared by <see cref="FullTimeout"/> and <c>HttpTransport.SleepAsync</c>.
/// </summary>
/// <remarks>
/// <c>Task.Delay</c> computes its due time from <c>Environment.TickCount64</c>, a coarse clock
/// that can lag the real time, so it can wake up to one tick early; the wait then asks again
/// for the rest. A single <c>Task.Delay</c> takes at most <see cref="MaxSingleDelay"/> (about
/// 49.7 days) and throws above it, so a longer wait is made of several capped ones.
/// </remarks>
internal static class MonotonicWait
{
    /// <summary>The longest wait <c>Task.Delay</c> accepts: <c>uint.MaxValue - 1</c> ms.</summary>
    internal static readonly TimeSpan MaxSingleDelay = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    /// <summary>
    /// The <see cref="Stopwatch"/> timestamp <paramref name="duration"/> after
    /// <paramref name="start"/>, rounded up, saturating at <see cref="long.MaxValue"/> (a deadline
    /// that is never reached) instead of overflowing.
    /// </summary>
    internal static long Deadline(long start, TimeSpan duration)
    {
        // Compare on the double before casting: (long) of a double at or above 2^63 is
        // platform-defined.
        var ticks = Math.Ceiling(duration.TotalSeconds * Stopwatch.Frequency);
        if (ticks >= long.MaxValue)
            return long.MaxValue;
        var span = (long)ticks;
        return span > 0 && start > long.MaxValue - span ? long.MaxValue : start + span;
    }

    /// <summary>
    /// The next delay to ask for at <paramref name="now"/>: the time left to
    /// <paramref name="deadline"/>, rounded up to the millisecond and capped at
    /// <see cref="MaxSingleDelay"/>; <see langword="null"/> once the deadline has passed.
    /// </summary>
    internal static TimeSpan? NextDelay(long deadline, long now)
    {
        var left = deadline - now;
        if (left <= 0)
            return null;
        var leftMs = Math.Ceiling(left * 1000.0 / Stopwatch.Frequency);
        return leftMs >= MaxSingleDelay.TotalMilliseconds ? MaxSingleDelay : TimeSpan.FromMilliseconds(leftMs);
    }

    /// <summary>
    /// Waits until <paramref name="deadline"/> with <paramref name="delay"/>, waiting again for
    /// the rest whenever it wakes early. Returns <see langword="false"/> as soon as
    /// <paramref name="token"/> is cancelled, without throwing.
    /// </summary>
    internal static async Task<bool> UntilAsync(
        long deadline, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken token)
    {
        while (NextDelay(deadline, Stopwatch.GetTimestamp()) is { } next)
        {
            var wait = delay(next, token);
            // A cancelled wait is the normal way out: no exception thrown and caught for it.
            await wait.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            if (token.IsCancellationRequested)
                return false;
            wait.GetAwaiter().GetResult(); // any other failure of the delay surfaces
        }

        return true;
    }
}
