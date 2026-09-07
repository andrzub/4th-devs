using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _02_04_zadanie.Mailbox;

/// <summary>
/// Raw HTTP access to the zmail API. Every call goes through one gate, so several researchers
/// working in parallel can never exceed the request budget or burst past a rate limit.
/// Retries and budget accounting live here rather than in the model's head: a researcher that
/// has to reason about HTTP 429 wastes an iteration on something code does better.
/// Each call is appended to a JSONL log with the API key redacted.
/// </summary>
public sealed class ZmailClient
{
    private const int MaxAttempts = 4;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _endpoint;
    private readonly string _apiKey;
    private readonly string _logPath;
    private readonly int _maxRequests;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private int _requestsUsed;
    private int? _serverRequestCounter;

    public ZmailClient(string hubBaseUrl, string apiKey, string logPath, int maxRequests)
    {
        _endpoint = hubBaseUrl.TrimEnd('/') + "/api/zmail";
        _apiKey = apiKey;
        _logPath = logPath;
        _maxRequests = maxRequests;
    }

    public int RequestsUsed => Volatile.Read(ref _requestsUsed);

    public int RequestsRemaining => Math.Max(0, _maxRequests - RequestsUsed);

    /// <summary>Latest request counter reported by the API itself, when it sent one.</summary>
    public int? ServerRequestCounter => _serverRequestCounter;

    public string BudgetLine =>
        $"zmail requests: {RequestsUsed} used, {RequestsRemaining} left of {_maxRequests}" +
        (_serverRequestCounter is { } counter ? $" (API-side counter: {counter})" : "");

    public Task<string> HelpAsync(CancellationToken cancellationToken = default) =>
        CallAsync(new JsonObject { ["action"] = "help", ["page"] = 1 }, cancellationToken);

    public Task<string> GetInboxAsync(int page, int perPage, CancellationToken cancellationToken = default) =>
        CallAsync(new JsonObject { ["action"] = "getInbox", ["page"] = page, ["perPage"] = perPage }, cancellationToken);

    public Task<string> GetThreadAsync(string threadId, CancellationToken cancellationToken = default) =>
        CallAsync(new JsonObject { ["action"] = "getThread", ["threadID"] = threadId }, cancellationToken);

    public Task<string> SearchAsync(string query, int page, int perPage, CancellationToken cancellationToken = default) =>
        CallAsync(new JsonObject { ["action"] = "search", ["query"] = query, ["page"] = page, ["perPage"] = perPage }, cancellationToken);

    public Task<string> GetMessagesAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
    {
        var idArray = new JsonArray();
        foreach (var id in ids)
            idArray.Add(id);

        return CallAsync(new JsonObject { ["action"] = "getMessages", ["ids"] = idArray }, cancellationToken);
    }

    /// <summary>Clears the API-side request counter for this key. Only ever called explicitly.</summary>
    public Task<string> ResetAsync(CancellationToken cancellationToken = default) =>
        CallAsync(new JsonObject { ["action"] = "reset" }, cancellationToken);

    private async Task<string> CallAsync(JsonObject payload, CancellationToken cancellationToken)
    {
        var action = payload["action"]?.ToString() ?? "?";
        payload["apikey"] = _apiKey;
        var json = payload.ToJsonString();

        await _gate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                if (RequestsUsed >= _maxRequests)
                    throw new ZmailBudgetExceededException(
                        $"Local zmail request budget exhausted ({_maxRequests} requests). Raise 'Mailbox:MaxZmailRequests' " +
                        "or run with --reset to clear the API-side counter.");

                Interlocked.Increment(ref _requestsUsed);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync(_endpoint, content, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                RememberServerCounter(body);
                Log(action, payload, (int)response.StatusCode, body, attempt);

                var retryable = response.StatusCode is System.Net.HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500;

                if (retryable && attempt < MaxAttempts)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(4 * attempt);
                    Console.WriteLine($"  [zmail] HTTP {(int)response.StatusCode} on '{action}', retrying in {delay.TotalSeconds:F0}s (attempt {attempt}/{MaxAttempts})...");
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                // Non-2xx bodies carry the API's own error text, which is more precise than any
                // status-code paraphrase, so they are returned to the caller as-is.
                return body;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private void RememberServerCounter(string body)
    {
        try
        {
            if (ZmailParser.ReadRequestCounter(body) is { } counter)
                _serverRequestCounter = counter;
        }
        catch (JsonException)
        {
            // Not JSON; nothing to read. The body is logged and returned regardless.
        }
    }

    private void Log(string action, JsonObject payload, int status, string body, int attempt)
    {
        var redactedPayload = JsonNode.Parse(payload.ToJsonString())!.AsObject();
        redactedPayload["apikey"] = "***";

        var entry = new JsonObject
        {
            ["timestamp"] = DateTimeOffset.Now.ToString("O"),
            ["kind"] = "zmail",
            ["action"] = action,
            ["attempt"] = attempt,
            ["requestsUsed"] = RequestsUsed,
            ["status"] = status,
            ["payload"] = redactedPayload,
            ["body"] = body.Replace(_apiKey, "***")
        };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_logPath))!);
        File.AppendAllText(_logPath, entry.ToJsonString() + Environment.NewLine);
    }
}
