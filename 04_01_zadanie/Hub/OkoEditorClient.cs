using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _04_01_zadanie.Hub;

public sealed record HubReply(int Status, string Body)
{
    public bool IsSuccess => Status is >= 200 and < 300;
}

/// <summary>
/// Every call this run makes to the okoeditor API. Calls are serialised, spaced apart and counted
/// here rather than left to the model: each edit is a request against the same budget as the
/// verification, and a run that spends its calls on retries has no way back.
/// </summary>
public sealed class OkoEditorClient(string baseUrl, string apiKey, string logPath, int maxRequests, double minSecondsBetweenRequests)
{
    private const int MaxAttempts = 4;
    private const string TaskName = "okoeditor";

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _minInterval = TimeSpan.FromSeconds(minSecondsBetweenRequests);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public int RequestsSent { get; private set; }

    public int UpdatesSent { get; private set; }

    public int RemainingRequests => maxRequests - RequestsSent;

    public Task<HubReply> HelpAsync(CancellationToken cancellationToken = default) =>
        PostAsync(new JsonObject { ["action"] = "help" }, cancellationToken);

    /// <summary>
    /// Sends one edit. The fields are passed exactly as the API documents them; nothing is filled
    /// in on the model's behalf, because an update that carries a field the caller did not mean to
    /// change overwrites live data with a guess.
    /// </summary>
    public async Task<HubReply> UpdateAsync(string page, string id, string? title, string? content, string? done, CancellationToken cancellationToken = default)
    {
        var answer = new JsonObject
        {
            ["page"] = page,
            ["id"] = id,
            ["action"] = "update"
        };

        if (content is not null)
            answer["content"] = content;
        if (title is not null)
            answer["title"] = title;
        if (done is not null)
            answer["done"] = done;

        var reply = await PostAsync(answer, cancellationToken);
        UpdatesSent++;
        return reply;
    }

    public Task<HubReply> DoneAsync(CancellationToken cancellationToken = default) =>
        PostAsync(new JsonObject { ["action"] = "done" }, cancellationToken);

    private async Task<HubReply> PostAsync(JsonObject answer, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (RequestsSent >= maxRequests)
                throw new HubBudgetExceededException($"Request budget spent: {maxRequests} calls already made to the okoeditor API. The run stops here.");

            var payload = new JsonObject { ["apikey"] = apiKey, ["task"] = TaskName, ["answer"] = answer.DeepClone() };
            var json = payload.ToJsonString();

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

    private void Log(JsonObject answer, int status, string body)
    {
        var entry = new JsonObject
        {
            ["at"] = DateTimeOffset.Now.ToString("O"),
            ["answer"] = answer.DeepClone(),
            ["status"] = status,
            ["body"] = Redact(body)
        };

        File.AppendAllText(logPath, entry.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + Environment.NewLine);
    }

    private string Redact(string text) => apiKey.Length > 0 ? text.Replace(apiKey, "***") : text;
}
