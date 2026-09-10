using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _03_01_zadanie.Hub;

public sealed record HubSubmission(int StatusCode, string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public string Render() => $"HTTP {StatusCode}: {Body}";
}

/// <summary>
/// The one call in this project that changes anything outside the machine: the answer for the
/// "evaluation" task. Every request and reply is appended to a JSONL log with the API key
/// redacted, so a run can be reconstructed later.
/// </summary>
public sealed class HubClient(string hubBaseUrl, string apiKey, string logPath)
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _hubBaseUrl = hubBaseUrl.TrimEnd('/');

    public async Task<HubSubmission> SubmitRecheckAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken = default)
    {
        var recheck = new JsonArray();
        foreach (var id in ids)
            recheck.Add(id);

        var payload = new JsonObject
        {
            ["apikey"] = apiKey,
            ["task"] = "evaluation",
            ["answer"] = new JsonObject { ["recheck"] = recheck }
        };

        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"{_hubBaseUrl}/verify", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Log(new JsonObject
        {
            ["kind"] = "verify",
            ["ids"] = ids.Count,
            ["status"] = (int)response.StatusCode,
            ["body"] = Redact(body)
        });

        return new HubSubmission((int)response.StatusCode, body);
    }

    private string Redact(string text) => apiKey.Length > 0 ? text.Replace(apiKey, "***") : text;

    private void Log(JsonObject entry)
    {
        entry["timestamp"] = DateTimeOffset.Now.ToString("O");
        File.AppendAllText(logPath, entry.ToJsonString(new JsonSerializerOptions()) + Environment.NewLine);
    }
}
