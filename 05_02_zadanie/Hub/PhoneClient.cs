using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _05_02_zadanie.Hub;

/// <summary>
/// Every request this run makes to the phonecall task: counted and logged. The log keeps each request and
/// reply with long Base64 cut down to a prefix and the API key redacted; the recordings themselves are saved
/// whole by the call recorder.
/// </summary>
public sealed class PhoneClient(string baseUrl, string apiKey, string logPath) : Call.IOperatorChannel
{
    private const string TaskName = "phonecall";
    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _baseUrl = baseUrl.TrimEnd('/');

    public int RequestsSent { get; private set; }

    /// <summary>Opens a call. Resending it is harmless: an unanswered start only opens a call nobody talks in.</summary>
    public Task<HubReply> StartAsync(CancellationToken cancellationToken = default) =>
        SendAsync(new JsonObject { ["action"] = "start" }, safeToResend: true, cancellationToken);

    /// <summary>
    /// One turn of the conversation. It is resent only after a 429, which the hub turns away before handling:
    /// after any other failure the operator may already have heard it, and saying it twice can burn the call.
    /// The hub hears only the recording, so <paramref name="spokenText"/> is ignored here.
    /// </summary>
    public Task<HubReply> SayAsync(byte[] recording, string spokenText, CancellationToken cancellationToken = default) =>
        SendAsync(new JsonObject { ["audio"] = Convert.ToBase64String(recording) }, safeToResend: false, cancellationToken);

    private async Task<HubReply> SendAsync(JsonObject answer, bool safeToResend, CancellationToken cancellationToken)
    {
        var json = new JsonObject { ["apikey"] = apiKey, ["task"] = TaskName, ["answer"] = answer.DeepClone() }.ToJsonString();

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync($"{_baseUrl}/verify", content, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                RequestsSent++;
                AppendLog(answer, (int)response.StatusCode, body);

                var turnedAway = response.StatusCode == HttpStatusCode.TooManyRequests
                    || (safeToResend && response.StatusCode == HttpStatusCode.ServiceUnavailable);
                if (turnedAway && attempt < MaxAttempts)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * attempt);
                    Console.WriteLine($"  [hub] {(int)response.StatusCode}, resending in {delay.TotalSeconds:F0}s (attempt {attempt}/{MaxAttempts})...");
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                return new HubReply((int)response.StatusCode, body);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                RequestsSent++;
                AppendLog(answer, 0, $"transport error: {ex.Message}");
                if (!safeToResend || attempt >= MaxAttempts)
                    throw new HubTransportException($"No reply from the hub to {JsonPreview.Shorten(answer).ToJsonString()}: {ex.Message}", ex);

                Console.WriteLine($"  [hub] transport error, resending (attempt {attempt}/{MaxAttempts}): {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
            }
        }
    }

    private void AppendLog(JsonObject answer, int status, string body) =>
        File.AppendAllText(logPath, LogLine(answer, status, body, apiKey) + Environment.NewLine);

    internal static string LogLine(JsonObject answer, int status, string body, string apiKey)
    {
        var entry = new JsonObject
        {
            ["at"] = DateTimeOffset.Now.ToString("O"),
            ["answer"] = JsonPreview.Shorten(answer),
            ["status"] = status,
            ["body"] = JsonPreview.ShortenLongStrings(body)
        };

        var line = entry.ToJsonString(Readable);
        return apiKey.Length > 0 ? line.Replace(apiKey, "***") : line;
    }
}
