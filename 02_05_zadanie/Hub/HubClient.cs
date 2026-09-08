using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _02_05_zadanie.Hub;

public sealed record HubSubmission(int Number, int StatusCode, string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>
    /// What the model gets to read. The hub's error messages are precise and name the exact
    /// field it dislikes, so they travel back unedited.
    /// </summary>
    public string Render() => $"HTTP {StatusCode}: {Body}";
}

public sealed class HubBudgetExceededException(string message) : Exception(message);

/// <summary>
/// Raw HTTP access to the AI_devs hub: the reconnaissance map and the drone instruction
/// submissions. Every call is appended to a JSONL log with the API key redacted, and the
/// number of submissions is capped here rather than left to the model's judgement.
/// </summary>
public sealed class HubClient(string hubBaseUrl, string apiKey, string cacheDirectory, string logPath, int maxSubmissions)
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _hubBaseUrl = hubBaseUrl.TrimEnd('/');
    private readonly SemaphoreSlim _gate = new(1, 1);

    public int SubmissionCount { get; private set; }
    public int RemainingSubmissions => maxSubmissions - SubmissionCount;

    public async Task<byte[]> GetMapPngAsync(bool refresh = false, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(cacheDirectory);
        var cachePath = Path.Combine(cacheDirectory, "drone-map.png");

        if (!refresh && File.Exists(cachePath))
            return await File.ReadAllBytesAsync(cachePath, cancellationToken);

        var url = $"{_hubBaseUrl}/data/{apiKey}/drone.png";
        using var response = await _http.GetAsync(url, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        Log(new JsonObject
        {
            ["kind"] = "map-fetch",
            ["status"] = (int)response.StatusCode,
            ["bytes"] = bytes.Length
        });

        response.EnsureSuccessStatusCode();
        await File.WriteAllBytesAsync(cachePath, bytes, cancellationToken);
        return bytes;
    }

    public async Task<HubSubmission> SendInstructionsAsync(IReadOnlyList<string> instructions, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (SubmissionCount >= maxSubmissions)
                throw new HubBudgetExceededException($"Submission budget spent: {maxSubmissions} instruction sets already sent to the hub. The run stops here.");

            var list = new JsonArray();
            foreach (var instruction in instructions)
                list.Add(instruction);

            var payload = new JsonObject
            {
                ["apikey"] = apiKey,
                ["task"] = "drone",
                ["answer"] = new JsonObject { ["instructions"] = list }
            };

            using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync($"{_hubBaseUrl}/verify", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            SubmissionCount++;

            Log(new JsonObject
            {
                ["kind"] = "instructions",
                ["number"] = SubmissionCount,
                ["instructions"] = JsonNode.Parse(list.ToJsonString()),
                ["status"] = (int)response.StatusCode,
                ["body"] = Redact(body)
            });

            return new HubSubmission(SubmissionCount, (int)response.StatusCode, body);
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
