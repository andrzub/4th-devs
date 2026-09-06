using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _02_03_zadanie.Hub;

/// <summary>
/// Raw HTTP access to the AI_devs hub: plant log download and digest submissions.
/// Every call is appended to a JSONL log with the API key redacted.
/// </summary>
public class HubClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _hubBaseUrl;
    private readonly string _apiKey;
    private readonly string _logPath;

    public HubClient(string hubBaseUrl, string apiKey, string logPath)
    {
        _hubBaseUrl = hubBaseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _logPath = logPath;
    }

    /// <summary>
    /// Downloads the full plant log, or reuses the cached copy. The file is large and static,
    /// so one download is plenty; <paramref name="refresh"/> forces a new one.
    /// </summary>
    public async Task<string> GetFailureLogAsync(string cachePath, bool refresh, CancellationToken cancellationToken = default)
    {
        if (!refresh && File.Exists(cachePath))
        {
            Console.WriteLine($"Using cached log '{cachePath}'.");
            return await File.ReadAllTextAsync(cachePath, cancellationToken);
        }

        Console.WriteLine("Downloading failure.log from the hub...");
        using var response = await _http.GetAsync($"{_hubBaseUrl}/data/{_apiKey}/failure.log", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Log(new JsonObject
        {
            ["kind"] = "log-fetch",
            ["status"] = (int)response.StatusCode,
            ["bytes"] = body.Length
        });

        response.EnsureSuccessStatusCode();

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cachePath))!);
        await File.WriteAllTextAsync(cachePath, body, cancellationToken);
        return body;
    }

    /// <summary>
    /// Submits one digest to /verify. Returns the raw response body: the technicians' feedback
    /// is precise, so paraphrasing it for the model would only lose information.
    /// </summary>
    public async Task<string> SubmitLogsAsync(string logs, int localTokenCount, CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject
        {
            ["apikey"] = _apiKey,
            ["task"] = "failure",
            ["answer"] = new JsonObject { ["logs"] = logs }
        };

        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"{_hubBaseUrl}/verify", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Log(new JsonObject
        {
            ["kind"] = "submit",
            ["lines"] = logs.Split('\n').Length,
            ["localTokens"] = localTokenCount,
            ["status"] = (int)response.StatusCode,
            ["body"] = Redact(body)
        });

        return $"HTTP {(int)response.StatusCode}: {body}";
    }

    private string Redact(string text) => text.Replace(_apiKey, "***");

    private void Log(JsonObject entry)
    {
        entry["timestamp"] = DateTimeOffset.Now.ToString("O");
        File.AppendAllText(_logPath, entry.ToJsonString(new JsonSerializerOptions()) + Environment.NewLine);
    }
}
