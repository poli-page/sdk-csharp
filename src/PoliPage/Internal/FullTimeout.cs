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
/// waits again for the rest when a delay returns early, as <c>HttpTransport.SleepAsync</c> does.
/// </remarks>
internal sealed class FullTimeout : IDisposable
{
    private readonly CancellationTokenSource _fired = new();
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Starts the timer now.</summary>
    /// <param name="timeout">The bound; <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> never fires.</param>
    /// <param name="delay">How the timer waits (a test passes one that wakes early).</param>
    public FullTimeout(TimeSpan timeout, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        if (timeout == System.Threading.Timeout.InfiniteTimeSpan)
            return;
        _ = RunAsync(timeout, delay ?? Task.Delay);
    }

    public CancellationToken Token => _fired.Token;

    public bool IsCancellationRequested => _fired.IsCancellationRequested;

    private async Task RunAsync(TimeSpan timeout, Func<TimeSpan, CancellationToken, Task> delay)
    {
        var end = Stopwatch.GetTimestamp() + (long)Math.Ceiling(timeout.TotalSeconds * Stopwatch.Frequency);
        try
        {
            while (true)
            {
                var left = end - Stopwatch.GetTimestamp();
                if (left <= 0)
                    break;
                var leftMs = Math.Ceiling(left * 1000.0 / Stopwatch.Frequency);
                await delay(TimeSpan.FromMilliseconds(leftMs), _stop.Token).ConfigureAwait(false);
            }

            _fired.Cancel();
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // Disposed before the timeout: nothing fires.
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
