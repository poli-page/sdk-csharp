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

        // Use long arithmetic to avoid TimeSpan overflow on large attempt numbers.
        // Math.Pow(2, attempt-1) is fine for small attempt counts; cap explicitly.
        var ticks = (long)(baseDelay.Ticks * Math.Pow(2, attempt - 1) * jitterFactor);
        var raw = TimeSpan.FromTicks(Math.Max(0, ticks));
        return raw > MaxDelay ? MaxDelay : raw;
    }
}
