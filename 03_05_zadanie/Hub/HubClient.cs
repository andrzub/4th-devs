using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _03_05_zadanie.Hub;

public sealed class HubBudgetExceededException(string message) : Exception(message);

public sealed record HubReply(int Status, string Body);

/// <summary>
/// Every call this run makes to the hub. The tool endpoints answer 429 after a short burst and stay
/// closed for about a minute, so calls are serialised, spaced apart and counted here rather than
/// left to the model: a limit hit inside the loop costs an iteration and the agent's picture of what
/// it already knows, while waiting three seconds costs nothing.
/// </summary>
public sealed class HubClient(string baseUrl, string apiKey, string logPath, int maxRequests, double minSecondsBetweenRequests)
{
    private const int MaxAttempts = 4;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _minInterval = TimeSpan.FromSeconds(minSecondsBetweenRequests);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public int RequestsSent { get; private set; }

    public int SubmissionsSent { get; private set; }

    public int RemainingRequests => maxRequests - RequestsSent;

    /// <summary>Calls one of the hub's tool endpoints with the only parameter they all take.</summary>
    public Task<HubReply> AskToolAsync(string path, string query, CancellationToken cancellationToken = default) =>
        PostJsonAsync("tool", path, new JsonObject { ["apikey"] = apiKey, ["query"] = query }, cancellationToken);

    public async Task<HubReply> SubmitAsync(IReadOnlyList<string> instructions, CancellationToken cancellationToken = default)
    {
        var answer = new JsonArray();
        foreach (var instruction in instructions)
            answer.Add(instruction);

        var payload = new JsonObject { ["apikey"] = apiKey, ["task"] = "savethem", ["answer"] = answer };
        var reply = await PostJsonAsync("verify", "/verify", payload, cancellationToken);
        SubmissionsSent++;
        return reply;
    }

    /// <summary>
    /// Reads the state behind the graphical preview. It only exists once a route has been sent, so
    /// this is a post-mortem of the last attempt: position, mode and both resources after every step.
    /// </summary>
    public async Task<HubReply> ReadPreviewAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WaitOutMinIntervalAsync(cancellationToken);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string> { ["key"] = apiKey });
            using var response = await _http.PostAsync($"{_baseUrl}/savethem_backend.php", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            Log("preview", "/savethem_backend.php", null, (int)response.StatusCode, body);
            return new HubReply((int)response.StatusCode, body);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<HubReply> PostJsonAsync(string kind, string path, JsonObject payload, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (RequestsSent >= maxRequests)
                throw new HubBudgetExceededException($"Request budget spent: {maxRequests} calls already made to the hub. The run stops here.");

            var json = payload.ToJsonString();
            var query = payload["query"]?.GetValue<string>();

            for (var attempt = 1; ; attempt++)
            {
                await WaitOutMinIntervalAsync(cancellationToken);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync($"{_baseUrl}{path}", content, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                RequestsSent++;
                Log(kind, path, query, (int)response.StatusCode, body);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < MaxAttempts)
                {
                    var delay = TimeSpan.FromSeconds(30 * attempt);
                    Console.WriteLine($"  [hub] 429 rate limited, waiting {delay.TotalSeconds:F0}s (attempt {attempt}/{MaxAttempts})...");
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                return new HubReply((int)response.StatusCode, body);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task WaitOutMinIntervalAsync(CancellationToken cancellationToken)
    {
        if (_minInterval <= TimeSpan.Zero)
            return;

        var wait = _lastRequestAt + _minInterval - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, cancellationToken);

        _lastRequestAt = DateTimeOffset.UtcNow;
    }

    private void Log(string kind, string path, string? query, int statusCode, string body)
    {
        var entry = new JsonObject
        {
            ["timestamp"] = DateTimeOffset.Now.ToString("O"),
            ["kind"] = kind,
            ["path"] = path,
            ["query"] = query,
            ["number"] = RequestsSent,
            ["status"] = statusCode,
            ["body"] = body.Replace(apiKey, "***")
        };

        File.AppendAllText(logPath, entry.ToJsonString(new JsonSerializerOptions()) + Environment.NewLine);
    }
}
