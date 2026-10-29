using System.Net.Http.Headers;

namespace PoliPage.Internal;

/// <summary>
/// Reads the <c>Retry-After</c> header of a failed answer (spec §9.2): integer seconds or
/// an HTTP-date, clamped to 0–30 s. <c>Retry-After-Ms</c> is never read.
/// </summary>
internal static class RetryAfterParser
{
    /// <summary>
    /// The delay the header asks for, or <see langword="null"/> when it is absent or is
    /// neither integer seconds nor an HTTP-date (the backoff applies then).
    /// </summary>
    internal static TimeSpan? Parse(HttpResponseHeaders headers, DateTimeOffset now)
    {
        // The raw value: the typed RetryAfter property drops a delta-seconds value too large
        // for an int, which must clamp to the cap rather than fall back to the backoff.
        if (!headers.NonValidated.TryGetValues("Retry-After", out var values))
            return null;

        string? raw = null;
        foreach (var value in values)
        {
            raw = value;
            break;
        }

        return raw is null ? null : Parse(raw, now);
    }

    internal static TimeSpan? Parse(string raw, DateTimeOffset now)
    {
        var value = raw.Trim();
        if (value.Length == 0)
            return null;

        if (IsAsciiDigits(value))
        {
            // Any number of digits: a value past the cap, however long, is the cap, so a
            // huge integer never overflows. Past leading zeros, three digits are >= 100 s.
            var significant = value.TrimStart('0');
            if (significant.Length > 2)
                return Backoff.MaxDelay;
            var seconds = significant.Length == 0
                ? 0
                : int.Parse(significant, System.Globalization.CultureInfo.InvariantCulture);
            var delay = TimeSpan.FromSeconds(seconds);
            return delay > Backoff.MaxDelay ? Backoff.MaxDelay : delay;
        }

        // HTTP-date (IMF-fixdate, RFC 850 or asctime), as the BCL header parser reads it.
        if (RetryConditionHeaderValue.TryParse(value, out var parsed) && parsed.Date is { } date)
        {
            var left = date - now;
            if (left <= TimeSpan.Zero)
                return TimeSpan.Zero;
            return left > Backoff.MaxDelay ? Backoff.MaxDelay : left;
        }

        return null;
    }

    private static bool IsAsciiDigits(string value)
    {
        foreach (var c in value)
        {
            if (c is < '0' or > '9')
                return false;
        }

        return true;
    }
}
