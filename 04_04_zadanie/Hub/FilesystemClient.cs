using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using _04_04_zadanie.Filesystem;

namespace _04_04_zadanie.Hub;

public sealed record HubReply(int Status, string Body)
{
    public bool IsSuccess => Status is >= 200 and < 300;

    public string Describe() => $"HTTP {Status}{(Code is { } code ? $" (code {code})" : "")}{Environment.NewLine}{Body}";

    /// <summary>The hub's own result code when the body is JSON with a numeric "code"; null otherwise.</summary>
    public int? Code
    {
        get
        {
            try
            {
                using var doc = JsonDocument.Parse(Body);
                return doc.RootElement.ValueKind == JsonValueKind.Object
                       && doc.RootElement.TryGetProperty("code", out var code)
                       && code.ValueKind == JsonValueKind.Number
                    ? code.GetInt32()
                    : null;
            }
            catch (JsonException)
            {
                return null;
            }
        }
    }
}

/// <summary>
/// Every call this run makes to the filesystem API. Calls are serialised, spaced apart, counted
/// and logged here rather than left to the model. The answer is passed through untouched: a single
/// action is an object, a batch is an array of actions, exactly as the task documents them.
/// </summary>
public sealed class FilesystemClient(string baseUrl, string apiKey, string logPath, int maxRequests, double minSecondsBetweenRequests)
{
    private const int MaxAttempts = 4;
    private const string TaskName = "filesystem";

    private static readonly JsonSerializerOptions RelaxedJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _minInterval = TimeSpan.FromSeconds(minSecondsBetweenRequests);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public int RequestsSent { get; private set; }

    public int RemainingRequests => maxRequests - RequestsSent;

    public Task<HubReply> HelpAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new JsonObject { ["action"] = "help" }, cancellationToken);

    public Task<HubReply> SendActionAsync(string action, CancellationToken cancellationToken = default) =>
        SendAsync(new JsonObject { ["action"] = action }, cancellationToken);

    public Task<HubReply> ListFilesAsync(string path = "/", CancellationToken cancellationToken = default) =>
        SendAsync(new JsonObject { ["action"] = "listFiles", ["path"] = path }, cancellationToken);

    public Task<HubReply> ResetAsync(CancellationToken cancellationToken = default) => SendActionAsync(ApiVocabulary.Default.Reset, cancellationToken);

    public Task<HubReply> DoneAsync(CancellationToken cancellationToken = default) => SendActionAsync(ApiVocabulary.Default.Done, cancellationToken);

    /// <summary>Sends one answer: a single action object or a batch array.</summary>
    public async Task<HubReply> SendAsync(JsonNode answer, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (RequestsSent >= maxRequests)
                throw new HubBudgetExceededException($"Request budget spent: {maxRequests} calls already made to the filesystem API. The run stops here.");

            var payload = new JsonObject { ["apikey"] = apiKey, ["task"] = TaskName, ["answer"] = answer.DeepClone() };
            var json = payload.ToJsonString(RelaxedJson);

            for (var attempt = 1; ; attempt++)
            {
                await WaitOutMinIntervalAsync(cancellationToken);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync($"{_baseUrl}/verify", content, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);

                RequestsSent++;
                Log(answer, (int)response.StatusCode, body);

                if (response.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable && attempt < MaxAttempts)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10 * attempt);
                    Console.WriteLine($"  [hub] {(int)response.StatusCode}, waiting {delay.TotalSeconds:F0}s (attempt {attempt}/{MaxAttempts})...");
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

    private void Log(JsonNode answer, int status, string body)
    {
        var entry = new JsonObject
        {
            ["at"] = DateTimeOffset.Now.ToString("O"),
            ["answer"] = answer.DeepClone(),
            ["status"] = status,
            ["body"] = Redact(body)
        };

        File.AppendAllText(logPath, entry.ToJsonString(RelaxedJson) + Environment.NewLine);
    }

    private string Redact(string text) => apiKey.Length > 0 ? text.Replace(apiKey, "***") : text;
}
