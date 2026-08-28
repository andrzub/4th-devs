using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace _01_05_zadanie.Railway;

public sealed record RailwayCallResult(int StatusCode, string Body, RateLimitSnapshot RateLimit, int Attempts, TimeSpan Waited);

public sealed class RailwayClientOptions
{
    public string VerifyUrl { get; init; } = "https://hub.ag3nts.org/verify";
    public string TaskName { get; init; } = "railway";
    public TimeSpan MinInterval { get; init; } = TimeSpan.FromSeconds(1);
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromMinutes(15);
    public int MaxAttemptsPerCall { get; init; } = 8;
    public string? LogPath { get; init; }
}

/// <summary>
/// Transport for the railway API. Both of the task's obstacles — deliberate 503 responses and a very
/// tight request budget — are absorbed here rather than delegated to the model: the lesson is explicit
/// that retry and limit handling must be deterministic code, and every model turn spent on a transient
/// error would burn part of the request budget. The agent therefore only sees responses that carry
/// information, and calls are serialised so parallel tool calls cannot overrun the limit.
/// </summary>
public sealed class RailwayClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RailwayClientOptions _options;
    private readonly string _apiKey;

    private RateLimitSnapshot? _lastSnapshot;
    private DateTimeOffset _lastCallAt = DateTimeOffset.MinValue;

    public RailwayClient(string apiKey, RailwayClientOptions options)
    {
        _apiKey = apiKey;
        _options = options;
    }

    /// <summary>Number of HTTP requests actually sent, retries included.</summary>
    public int RequestsSent { get; private set; }

    /// <summary>Total time spent sleeping for backoff and rate-limit windows.</summary>
    public TimeSpan TotalWaited { get; private set; }

    public async Task<RailwayCallResult> CallAsync(JsonObject answer, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await SendWithRetriesAsync(answer, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<RailwayCallResult> SendWithRetriesAsync(JsonObject answer, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["apikey"] = _apiKey,
            ["task"]   = _options.TaskName,
            ["answer"] = answer.DeepClone()
        }.ToJsonString();

        var waitedForThisCall = TimeSpan.Zero;

        for (var attempt = 1; ; attempt++)
        {
            waitedForThisCall += await RespectKnownLimitsAsync(cancellationToken);
            waitedForThisCall += await RespectMinIntervalAsync(cancellationToken);

            HttpStatusCode status;
            string body;
            RateLimitSnapshot snapshot;

            try
            {
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync(_options.VerifyUrl, content, cancellationToken);

                RequestsSent++;
                _lastCallAt = DateTimeOffset.UtcNow;

                status   = response.StatusCode;
                body     = await response.Content.ReadAsStringAsync(cancellationToken);
                snapshot = RateLimitSnapshot.FromResponse(response);
                _lastSnapshot = snapshot;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                if (attempt >= _options.MaxAttemptsPerCall)
                    throw;

                Console.WriteLine($"  [api] transport error ({ex.GetType().Name}: {ex.Message}) — attempt {attempt}, retrying");
                await LogAsync(payload, attempt, null, null, ex.Message, cancellationToken);
                waitedForThisCall += await SleepAsync(Backoff(attempt), "transport error", cancellationToken);
                continue;
            }

            Console.WriteLine($"  [api] {(int)status} | {snapshot.Describe()} | attempt {attempt}");
            await LogAsync(payload, attempt, (int)status, snapshot, body, cancellationToken);

            var isRetryable = status is HttpStatusCode.ServiceUnavailable or HttpStatusCode.TooManyRequests
                                     or HttpStatusCode.BadGateway or HttpStatusCode.GatewayTimeout;

            if (isRetryable && attempt < _options.MaxAttemptsPerCall)
            {
                // 503 is a simulated overload, so a plain backoff is right. 429 means the budget is
                // genuinely spent, so the headers decide and backoff is only the fallback.
                var wait = status == HttpStatusCode.TooManyRequests
                    ? snapshot.SuggestedWait() ?? Backoff(attempt)
                    : snapshot.RetryAfter ?? Backoff(attempt);

                waitedForThisCall += await SleepAsync(wait, $"HTTP {(int)status}", cancellationToken);
                continue;
            }

            TotalWaited += waitedForThisCall;
            return new RailwayCallResult((int)status, body, snapshot, attempt, waitedForThisCall);
        }
    }

    /// <summary>
    /// Waits out the window announced by the previous response before spending another request on a
    /// call that the API would only reject.
    /// </summary>
    private async Task<TimeSpan> RespectKnownLimitsAsync(CancellationToken cancellationToken)
    {
        if (_lastSnapshot is not { IsExhausted: true } snapshot)
            return TimeSpan.Zero;

        if (snapshot.SuggestedWait() is not { } wait)
            return TimeSpan.Zero;

        // A small grace period so we do not wake up a fraction of a second before the reset.
        return await SleepAsync(wait + TimeSpan.FromSeconds(1), "rate-limit budget exhausted", cancellationToken);
    }

    private async Task<TimeSpan> RespectMinIntervalAsync(CancellationToken cancellationToken)
    {
        var elapsed = DateTimeOffset.UtcNow - _lastCallAt;
        if (elapsed >= _options.MinInterval)
            return TimeSpan.Zero;

        var wait = _options.MinInterval - elapsed;
        await Task.Delay(wait, cancellationToken);
        return wait;
    }

    private static TimeSpan Backoff(int attempt)
    {
        var seconds = Math.Min(30, Math.Pow(2, attempt - 1));
        return TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(Random.Shared.Next(250, 1250));
    }

    private async Task<TimeSpan> SleepAsync(TimeSpan wait, string reason, CancellationToken cancellationToken)
    {
        if (wait <= TimeSpan.Zero)
            return TimeSpan.Zero;

        if (wait > _options.MaxWait)
        {
            Console.WriteLine($"  [api] {reason}: headers ask for {wait.TotalSeconds:0}s, capping the wait at {_options.MaxWait.TotalSeconds:0}s");
            wait = _options.MaxWait;
        }

        Console.WriteLine($"  [api] waiting {wait.TotalSeconds:0.#}s ({reason})");

        var remaining = wait;
        while (remaining > TimeSpan.Zero)
        {
            var slice = remaining > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : remaining;
            await Task.Delay(slice, cancellationToken);
            remaining -= slice;

            if (remaining > TimeSpan.Zero)
                Console.WriteLine($"  [api] still waiting, {remaining.TotalSeconds:0}s to go");
        }

        return wait;
    }

    private async Task LogAsync(string payload, int attempt, int? status, RateLimitSnapshot? snapshot, string body, CancellationToken cancellationToken)
    {
        if (_options.LogPath is not { } path)
            return;

        var entry = new JsonObject
        {
            ["ts"]      = DateTimeOffset.UtcNow.ToString("O"),
            ["attempt"] = attempt,
            ["request"] = JsonNode.Parse(payload),
            ["status"]  = status,
            ["body"]    = body
        };

        if (snapshot is not null)
        {
            var headers = new JsonObject();
            foreach (var (key, value) in snapshot.Headers)
                headers[key] = value;

            entry["headers"] = headers;
        }

        // The payload carries the API key; redact it so the log stays shareable.
        if (entry["request"]?["apikey"] is not null)
            entry["request"]!["apikey"] = "***";

        try
        {
            await File.AppendAllTextAsync(path, entry.ToJsonString() + Environment.NewLine, cancellationToken);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [api] could not write the log: {ex.Message}");
        }
    }
}
