using System.Text;
using System.Text.Json.Nodes;

namespace _02_04_zadanie.Hub;

/// <summary>
/// Submits the three recovered values to the AI_devs hub. Every call is appended to a JSONL
/// log with the API key redacted, and the response body is returned verbatim: the hub names
/// which value it rejects, and paraphrasing that for the coordinator would only lose detail.
/// </summary>
public sealed class HubClient(string hubBaseUrl, string apiKey, string logPath)
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _hubBaseUrl = hubBaseUrl.TrimEnd('/');

    public async Task<string> SubmitAnswerAsync(string date, string password, string confirmationCode, CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject
        {
            ["apikey"] = apiKey,
            ["task"] = "mailbox",
            ["answer"] = new JsonObject
            {
                ["password"] = password,
                ["date"] = date,
                ["confirmation_code"] = confirmationCode
            }
        };

        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"{_hubBaseUrl}/verify", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        Log(new JsonObject
        {
            ["kind"] = "submit",
            ["date"] = date,
            ["password"] = password,
            ["confirmation_code"] = confirmationCode,
            ["status"] = (int)response.StatusCode,
            ["body"] = body.Replace(apiKey, "***")
        });

        return $"HTTP {(int)response.StatusCode}: {body}";
    }

    private void Log(JsonObject entry)
    {
        entry["timestamp"] = DateTimeOffset.Now.ToString("O");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
        File.AppendAllText(logPath, entry.ToJsonString() + Environment.NewLine);
    }
}
