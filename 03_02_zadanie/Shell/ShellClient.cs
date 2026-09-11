using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace _03_02_zadanie.Shell;

/// <summary>
/// Raised when the machine cuts access. A ban also rebuilds the filesystem, so everything the run
/// has learned about the machine's state is stale from that moment: the run ends rather than waits.
/// </summary>
public sealed class ShellBanException(string message, TimeSpan? duration) : Exception(message)
{
    public TimeSpan? Duration { get; } = duration;
}

public sealed class ShellBudgetExceededException(string message) : Exception(message);

/// <summary>
/// The transport to the virtual machine: one HTTP request per command, never two at a time. Rate
/// limits, service errors and the request budget are handled here rather than by the agent, so a
/// 503 costs a few seconds instead of an iteration and a wrong turn in the reasoning.
/// </summary>
public sealed partial class ShellClient(string hubBaseUrl, string apiKey, string logPath, int maxRequests, int maxRetries = 4)
{
    [GeneratedRegex(@"\b(ban|banned|blocked|zablokowan\w*)\b", RegexOptions.IgnoreCase)]
    private static partial Regex BanRegex();

    [GeneratedRegex(@"(\d+)\s*(?:s\b|sec|second)", RegexOptions.IgnoreCase)]
    private static partial Regex BanDurationRegex();

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _endpoint = $"{hubBaseUrl.TrimEnd('/')}/api/shell";
    private readonly SemaphoreSlim _gate = new(1, 1);

    public int RequestCount { get; private set; }
    public int RemainingRequests => maxRequests - RequestCount;

    public async Task<ShellResponse> SendAsync(string command, CancellationToken cancellationToken = default)
    {
        // The machine keeps its own state between calls, so commands are serialised: two in flight
        // at once would make the mirrored working directory meaningless.
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (RequestCount >= maxRequests)
                throw new ShellBudgetExceededException($"Shell budget spent: {maxRequests} commands already sent. The run stops here rather than hammering the machine.");

            return await SendWithRetriesAsync(command, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<ShellResponse> SendWithRetriesAsync(string command, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var payload = new JsonObject { ["apikey"] = apiKey, ["cmd"] = command };
            using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            using var httpResponse = await _http.PostAsync(_endpoint, content, cancellationToken);
            var body = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            var limits = RateLimitSnapshot.FromResponse(httpResponse);

            RequestCount++;
            var response = ShellResponse.Parse((int)httpResponse.StatusCode, body);

            Log(new JsonObject
            {
                ["kind"] = "shell",
                ["number"] = RequestCount,
                ["attempt"] = attempt,
                ["cmd"] = command,
                ["status"] = response.HttpStatus,
                ["code"] = response.Code,
                ["rateLimit"] = limits.Describe(),
                ["body"] = Redact(body)
            });

            if (IsBan(response, out var ban))
                throw ban;

            var retryDelay = RetryDelayFor(response, limits, attempt);
            if (retryDelay is null || attempt > maxRetries)
                return response;

            Console.WriteLine($"[shell] HTTP {response.HttpStatus} ({limits.Describe()}), waiting {retryDelay.Value.TotalSeconds:0.#}s before retry {attempt}/{maxRetries}");
            await Task.Delay(retryDelay.Value, cancellationToken);
        }
    }

    /// <summary>Null when the response is final; otherwise how long to wait before trying again.</summary>
    private static TimeSpan? RetryDelayFor(ShellResponse response, RateLimitSnapshot limits, int attempt)
    {
        if (response.HttpStatus is not (429 or 500 or 502 or 503 or 504))
            return null;

        var suggested = limits.SuggestedWait();
        if (suggested is { } wait && wait > TimeSpan.Zero)
            return wait + TimeSpan.FromMilliseconds(500);

        return TimeSpan.FromSeconds(Math.Min(30, Math.Pow(2, attempt)));
    }

    private static bool IsBan(ShellResponse response, out ShellBanException ban)
    {
        ban = null!;
        if (!BanRegex().IsMatch(response.Message))
            return false;

        var match = BanDurationRegex().Match(response.Message);
        var duration = match.Success && int.TryParse(match.Groups[1].Value, out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : (TimeSpan?)null;

        ban = new ShellBanException(
            $"The machine cut off access: [{response.Code}] {response.Message}. A ban also rebuilds the filesystem, so everything learned about its state is now stale and the run ends here.",
            duration);

        return true;
    }

    private string Redact(string text) => text.Replace(apiKey, "***");

    private void Log(JsonObject entry)
    {
        entry["timestamp"] = DateTimeOffset.Now.ToString("O");
        File.AppendAllText(logPath, entry.ToJsonString(new JsonSerializerOptions()) + Environment.NewLine);
    }
}
