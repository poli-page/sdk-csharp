namespace PoliPage.Internal;

/// <summary>
/// Pure function helpers for computing retry delays with exponential backoff and jitter.
/// </summary>
internal static class Backoff
{
    // Spec §7: cap any single delay at 30s. Bumping it higher would let a hostile Retry-After
    // header stall the request beyond the user's typical request budget.
    internal static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Computes the delay before retry attempt <paramref name="attempt"/> (1-based: the
    /// first retry is attempt=1). Result = baseDelay × 2^(attempt-1) × jitterFactor,
    /// capped at <see cref="MaxDelay"/>. If <paramref name="retryAfter"/> is provided
    /// (from a Retry-After header), it overrides the computed delay and is also capped
    /// at <see cref="MaxDelay"/>.
    /// </summary>
    /// <param name="attempt">1-based retry attempt number (1 = first retry).</param>
    /// <param name="baseDelay">Base delay (e.g. <see cref="PoliPageClientOptions.RetryDelay"/>).</param>
    /// <param name="retryAfter">Optional server-supplied Retry-After value.</param>
    /// <param name="jitterFactor">Multiplier in [0.5, 1.5) from the caller's jitter source.</param>
    internal static TimeSpan ComputeDelay(int attempt, TimeSpan baseDelay, TimeSpan? retryAfter, double jitterFactor)
    {
        if (retryAfter is { } ra)
        {
            if (ra < TimeSpan.Zero) return TimeSpan.Zero;
            return ra > MaxDelay ? MaxDelay : ra;
        }

        // Cap on the double BEFORE casting to long. For large attempt counts the
        // product exceeds long.MaxValue, and (long)hugeDouble is platform-defined:
        // x86_64 returns long.MinValue, ARM64 saturates to long.MaxValue. Comparing
        // doubles dodges the cast entirely on the cap and zero branches.
        var rawTicks = baseDelay.Ticks * Math.Pow(2, attempt - 1) * jitterFactor;
        if (rawTicks >= MaxDelay.Ticks) return MaxDelay;
        if (rawTicks <= 0) return TimeSpan.Zero;
        return TimeSpan.FromTicks((long)rawTicks);
    }
}
