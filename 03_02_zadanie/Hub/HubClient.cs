using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _03_02_zadanie.Hub;

public sealed record HubSubmission(int StatusCode, string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>
    /// What the model gets to read. The hub's error messages name the exact field it dislikes,
    /// so they travel back unedited.
    /// </summary>
    public string Render() => $"HTTP {StatusCode}: {Body}";
}

public sealed class HubBudgetExceededException(string message) : Exception(message);

/// <summary>
/// Submissions of the confirmation code to the AI_devs hub. Every call is appended to a JSONL log
/// with the API key redacted, and the number of attempts is capped here rather than left to the
/// model's judgement.
/// </summary>
public sealed class HubClient(string hubBaseUrl, string apiKey, string logPath, int maxSubmissions)
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _hubBaseUrl = hubBaseUrl.TrimEnd('/');
    private readonly SemaphoreSlim _gate = new(1, 1);

    public int SubmissionCount { get; private set; }
    public int RemainingSubmissions => maxSubmissions - SubmissionCount;

    public async Task<HubSubmission> SubmitConfirmationAsync(string code, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (SubmissionCount >= maxSubmissions)
                throw new HubBudgetExceededException($"Submission budget spent: {maxSubmissions} codes already sent to the hub. The run stops here.");

            var payload = new JsonObject
            {
                ["apikey"] = apiKey,
                ["task"] = "firmware",
                ["answer"] = new JsonObject { ["confirmation"] = code }
            };

            using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync($"{_hubBaseUrl}/verify", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            SubmissionCount++;

            Log(new JsonObject
            {
                ["kind"] = "verify",
                ["number"] = SubmissionCount,
                ["confirmation"] = code,
                ["status"] = (int)response.StatusCode,
                ["body"] = Redact(body)
            });

            return new HubSubmission((int)response.StatusCode, body);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string Redact(string text) => text.Replace(apiKey, "***");

    private void Log(JsonObject entry)
    {
        entry["timestamp"] = DateTimeOffset.Now.ToString("O");
        File.AppendAllText(logPath, entry.ToJsonString(new JsonSerializerOptions()) + Environment.NewLine);
    }
}
