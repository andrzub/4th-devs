using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _02_02_zadanie.Hub;

/// <summary>
/// Raw HTTP access to the AI_devs hub: board image download and rotation submissions.
/// Every call (including image fetches) is appended to a JSONL log with the API key redacted.
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

    public async Task<byte[]> GetBoardPngAsync(bool reset, CancellationToken cancellationToken = default)
    {
        var url = $"{_hubBaseUrl}/data/{_apiKey}/electricity.png" + (reset ? "?reset=1" : "");
        using var response = await _http.GetAsync(url, cancellationToken);
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        Log(new JsonObject
        {
            ["kind"] = reset ? "board-reset" : "board-fetch",
            ["status"] = (int)response.StatusCode,
            ["bytes"] = bytes.Length
        });

        response.EnsureSuccessStatusCode();
        return bytes;
    }

    public async Task<byte[]> GetImageAsync(string url, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    /// <summary>
    /// Submits one 90° clockwise rotation of the given tile ("AxB") to /verify.
    /// Returns the raw response body — error messages from the hub are precise,
    /// so paraphrasing them for the model would only lose information.
    /// </summary>
    public async Task<string> RotateTileAsync(string tile, CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject
        {
            ["apikey"] = _apiKey,
            ["task"] = "electricity",
            ["answer"] = new JsonObject { ["rotate"] = tile }
        };

        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"{_hubBaseUrl}/verify", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Log(new JsonObject
        {
            ["kind"] = "rotate",
            ["tile"] = tile,
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
