using System.Globalization;

namespace _01_05_zadanie.Railway;

/// <summary>
/// Rate-limit state read from the headers of a single response. Header naming is not standardised
/// across APIs, so every field probes the spellings in common use and the reset value is accepted
/// as a delta, a unix timestamp (seconds or milliseconds) or an HTTP-date.
/// </summary>
public sealed class RateLimitSnapshot
{
    public int? Limit { get; init; }
    public int? Remaining { get; init; }
    public DateTimeOffset? ResetAt { get; init; }
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>Every response header, kept verbatim for the log.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    public bool IsExhausted => Remaining is 0;

    private static readonly string[] LimitHeaders     = ["x-ratelimit-limit", "ratelimit-limit", "x-rate-limit-limit"];
    private static readonly string[] RemainingHeaders = ["x-ratelimit-remaining", "ratelimit-remaining", "x-rate-limit-remaining"];
    private static readonly string[] ResetHeaders     = ["x-ratelimit-reset", "ratelimit-reset", "x-rate-limit-reset", "x-ratelimit-reset-after", "ratelimit-reset-after"];

    public static RateLimitSnapshot FromResponse(HttpResponseMessage response)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, values) in response.Headers)
            headers[key] = string.Join(", ", values);

        foreach (var (key, values) in response.Content.Headers)
            headers[key] = string.Join(", ", values);

        return new RateLimitSnapshot
        {
            Limit      = FirstInt(headers, LimitHeaders),
            Remaining  = FirstInt(headers, RemainingHeaders),
            ResetAt    = FirstReset(headers, ResetHeaders),
            RetryAfter = ParseRetryAfter(headers),
            Headers    = headers
        };
    }

    /// <summary>
    /// How long to wait before the next call is allowed, or null when nothing in the headers says to wait.
    /// </summary>
    public TimeSpan? SuggestedWait()
    {
        if (RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero)
            return retryAfter;

        if (ResetAt is { } resetAt)
        {
            var wait = resetAt - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
                return wait;
        }

        return null;
    }

    public string Describe()
    {
        var parts = new List<string>();

        if (Remaining is { } remaining)
            parts.Add(Limit is { } limit ? $"remaining {remaining}/{limit}" : $"remaining {remaining}");
        else if (Limit is { } limitOnly)
            parts.Add($"limit {limitOnly}");

        if (ResetAt is { } reset)
        {
            var inSeconds = Math.Max(0, (reset - DateTimeOffset.UtcNow).TotalSeconds);
            parts.Add($"reset in {inSeconds:0}s");
        }

        if (RetryAfter is { } retry)
            parts.Add($"retry-after {retry.TotalSeconds:0}s");

        return parts.Count > 0 ? string.Join(", ", parts) : "no rate-limit headers";
    }

    private static int? FirstInt(IReadOnlyDictionary<string, string> headers, string[] names)
    {
        foreach (var name in names)
        {
            if (headers.TryGetValue(name, out var raw) && int.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;
        }

        return null;
    }

    private static DateTimeOffset? FirstReset(IReadOnlyDictionary<string, string> headers, string[] names)
    {
        foreach (var name in names)
        {
            if (headers.TryGetValue(name, out var raw) && TryParseReset(raw.Trim(), out var resetAt))
                return resetAt;
        }

        return null;
    }

    private static bool TryParseReset(string raw, out DateTimeOffset resetAt)
    {
        resetAt = default;

        if (raw.Length == 0)
            return false;

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            // The same header name is used for all three conventions, so the magnitude decides:
            // epoch milliseconds, epoch seconds, or a plain number of seconds from now.
            resetAt = number switch
            {
                >= 1_000_000_000_000 => DateTimeOffset.FromUnixTimeMilliseconds((long)number),
                >= 1_000_000_000     => DateTimeOffset.FromUnixTimeSeconds((long)number),
                _                    => DateTimeOffset.UtcNow.AddSeconds(number)
            };
            return true;
        }

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var parsed))
        {
            resetAt = parsed;
            return true;
        }

        return false;
    }

    private static TimeSpan? ParseRetryAfter(IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue("retry-after", out var raw))
            return null;

        raw = raw.Trim();

        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
            return TimeSpan.FromSeconds(seconds);

        if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out var when))
        {
            var wait = when - DateTimeOffset.UtcNow;
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }
}
