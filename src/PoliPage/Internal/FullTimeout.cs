using System.Diagnostics;

namespace PoliPage.Internal;

/// <summary>
/// A timeout that never fires early (<c>R-WAIT-FULL</c>): <see cref="Token"/> is cancelled no
/// sooner than the timeout after construction, measured on the monotonic clock.
/// </summary>
/// <remarks>
/// <c>new CancellationTokenSource(timeout)</c> and <c>Task.Delay</c> compute their due time
/// from <c>Environment.TickCount64</c>, a coarse clock (about 15.6 ms on Windows, about 4 ms
/// on Linux) that can lag the real time, so they can fire up to one tick early. This timer
/// waits again for the rest when a delay returns early (<see cref="MonotonicWait"/>), and splits
/// a timeout longer than one <c>Task.Delay</c> can take into several waits.
/// </remarks>
internal sealed class FullTimeout : IDisposable
{
    private readonly CancellationTokenSource _fired = new();
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Starts the timer now.</summary>
    /// <param name="timeout">
    /// The bound, zero or more; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> never
    /// fires, and neither does a bound past the monotonic clock's range (such as
    /// <see cref="TimeSpan.MaxValue"/>).
    /// </param>
    /// <param name="delay">How the timer waits (a test passes one that wakes early).</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> is negative and not <see cref="System.Threading.Timeout.InfiniteTimeSpan"/>,
    /// as <c>new CancellationTokenSource(timeout)</c> rejects it.
    /// </exception>
    public FullTimeout(TimeSpan timeout, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        if (timeout == System.Threading.Timeout.InfiniteTimeSpan)
            return;
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, TimeSpan.Zero);
        _ = RunAsync(MonotonicWait.Deadline(Stopwatch.GetTimestamp(), timeout), delay ?? Task.Delay, _stop.Token);
    }

    public CancellationToken Token => _fired.Token;

    public bool IsCancellationRequested => _fired.IsCancellationRequested;

    private async Task RunAsync(long deadline, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken stop)
    {
        if (!await MonotonicWait.UntilAsync(deadline, delay, stop).ConfigureAwait(false))
            return; // disposed before the timeout: nothing fires

        try
        {
            _fired.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Disposed while the timer fired.
        }
    }

    public void Dispose()
    {
        try
        {
            _stop.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        _stop.Dispose();
        _fired.Dispose();
    }
}
