using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace _05_01_zadanie.Hub;

/// <summary>
/// Every call this run makes to the radiomonitoring API: serialised, spaced apart, counted and logged.
/// The log keeps the reply without its attachment payload, because a single Base64 file can be
/// megabytes long and the full reply is already saved by the capture store.
/// </summary>
public sealed class RadioClient(string baseUrl, string apiKey, string logPath, double minSecondsBetweenRequests)
{
    private const int MaxAttempts = 4;
    private const string TaskName = "radiomonitoring";
    private const int LoggedAttachmentPrefix = 80;

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    private readonly TimeSpan _minInterval = TimeSpan.FromSeconds(minSecondsBetweenRequests);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public int RequestsSent { get; private set; }

    public Task<HubReply> StartAsync(CancellationToken cancellationToken = default) => SendAsync(Action("start"), cancellationToken);

    public Task<HubReply> ListenAsync(CancellationToken cancellationToken = default) => SendAsync(Action("listen"), cancellationToken);

    /// <summary>Sends one answer; 429, 503 and transport failures are retried with a growing delay.</summary>
    public async Task<HubReply> SendAsync(JsonObject answer, CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject { ["apikey"] = apiKey, ["task"] = TaskName, ["answer"] = answer.DeepClone() };
        var json = payload.ToJsonString();

        for (var attempt = 1; ; attempt++)
        {
            await WaitOutMinIntervalAsync(cancellationToken);

            try
            {
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var response = await _http.PostAsync($"{_baseUrl}/verify", content, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                RequestsSent++;
                Log(answer, (int)response.StatusCode, body);

                if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable && attempt < MaxAttempts)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(10 * attempt);
                    Console.WriteLine($"  [hub] {(int)response.StatusCode}, waiting {delay.TotalSeconds:F0}s (attempt {attempt}/{MaxAttempts})...");
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                return new HubReply((int)response.StatusCode, body);
            }
            catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                RequestsSent++;
                Log(answer, 0, $"transport error: {ex.Message}");
                if (attempt >= MaxAttempts)
                    throw new HubTransportException($"No reply from the hub for action '{answer["action"]}': {ex.Message}", ex);

                Console.WriteLine($"  [hub] transport error, retrying (attempt {attempt}/{MaxAttempts}): {ex.Message}");
                await Task.Delay(TimeSpan.FromSeconds(5 * attempt), cancellationToken);
            }
        }
    }

    private static JsonObject Action(string action) => new() { ["action"] = action };

    private async Task WaitOutMinIntervalAsync(CancellationToken cancellationToken)
    {
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
            ["body"] = Redact(WithoutAttachmentPayload(body))
        };

        File.AppendAllText(logPath, entry.ToJsonString() + Environment.NewLine);
    }

    private static string WithoutAttachmentPayload(string body)
    {
        try
        {
            if (JsonNode.Parse(body) is JsonObject reply && reply["attachment"]?.GetValue<string>() is { } attachment)
            {
                reply["attachment"] = $"{attachment[..Math.Min(LoggedAttachmentPrefix, attachment.Length)]}... ({attachment.Length} Base64 chars, full reply in the capture store)";
                return reply.ToJsonString();
            }
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
        }

        return body;
    }

    private string Redact(string text) => apiKey.Length > 0 ? text.Replace(apiKey, "***") : text;
}
