using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _04_03_zadanie.Hub;

public sealed record HubReply(int Status, string Body, TimeSpan Elapsed)
{
    public bool IsSuccess => Status is >= 200 and < 300;

    /// <summary>The hub's own result code; negative means the action was refused even when HTTP says otherwise.</summary>
    public int? Code => TryParseJson()?["code"] is JsonValue value && value.TryGetValue<int>(out var code) ? code : null;

    public string? Message => TryParseJson()?["message"]?.GetValue<string>();

    public bool IsOk => IsSuccess && Code is >= 0;

    /// <summary>The body as JSON, or null when the hub answered with something else (an HTML error page, an empty body).</summary>
    public JsonNode? TryParseJson()
    {
        try
        {
            return JsonNode.Parse(Body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Every call this run makes to the hub. Two very different endpoints hide behind it: <c>/verify</c>,
/// where each action is a move in the operation and may spend action points, and the preview backend,
/// which only reports the state and costs nothing. Calls are serialised and spaced apart because the
/// limits are undocumented, and every one of them lands in the log with the key redacted.
/// </summary>
public sealed class DomatowoClient : IDisposable
{
    private const string TaskName = "domatowo";
    private const int MaxAttempts = 3;

    private static readonly JsonSerializerOptions LogOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _logPath;
    private readonly TimeSpan _minInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public DomatowoClient(string baseUrl, string apiKey, string logPath, int requestTimeoutSeconds, double minSecondsBetweenRequests)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _logPath = logPath;
        _minInterval = TimeSpan.FromSeconds(minSecondsBetweenRequests);
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(requestTimeoutSeconds) };
    }

    /// <summary>Requests sent to <c>/verify</c>, retries included.</summary>
    public int ActionsSent { get; private set; }

    /// <summary>Free reads of the preview backend.</summary>
    public int StateReadsSent { get; private set; }

    public Task<HubReply> HelpAsync(CancellationToken cancellationToken = default) =>
        SendActionAsync(new JsonObject { ["action"] = "help" }, cancellationToken);

    /// <summary>Where every unit stands according to the hub itself; free, and unlike the preview backend never behind a queued move.</summary>
    public Task<HubReply> GetObjectsAsync(CancellationToken cancellationToken = default) =>
        SendActionAsync(new JsonObject { ["action"] = "getObjects" }, cancellationToken);

    /// <summary>Every inspection so far; free.</summary>
    public Task<HubReply> GetLogsAsync(CancellationToken cancellationToken = default) =>
        SendActionAsync(new JsonObject { ["action"] = "getLogs" }, cancellationToken);

    public Task<HubReply> GetMapAsync(IReadOnlyCollection<string>? symbols = null, CancellationToken cancellationToken = default)
    {
        var answer = new JsonObject { ["action"] = "getMap" };
        if (symbols is { Count: > 0 })
            answer["symbols"] = new JsonArray(symbols.Select(symbol => (JsonNode)symbol).ToArray());

        return SendActionAsync(answer, cancellationToken);
    }

    /// <summary>
    /// Sends one action to <c>/verify</c>. Retried only on 429 and 503, the two answers that mean the
    /// action was never applied; anything else, a timeout included, is returned or thrown as is, because
    /// repeating an action that may have gone through would spend its points twice.
    /// </summary>
    public async Task<HubReply> SendActionAsync(JsonObject answer, CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject { ["apikey"] = _apiKey, ["task"] = TaskName, ["answer"] = answer.DeepClone() };
        var json = payload.ToJsonString();
        var actionName = answer["action"]?.GetValue<string>() ?? "?";

        await _gate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                await WaitOutMinIntervalAsync(cancellationToken);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var stopwatch = Stopwatch.StartNew();
                using var response = await _http.PostAsync($"{_baseUrl}/verify", content, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                stopwatch.Stop();

                ActionsSent++;
                Log("verify", actionName, answer, (int)response.StatusCode, body, stopwatch.Elapsed);

                if (IsNeverApplied(response.StatusCode) && attempt < MaxAttempts)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5 * attempt);
                    Console.WriteLine($"  [hub] {(int)response.StatusCode} on {actionName}, waiting {delay.TotalSeconds:F0}s (attempt {attempt}/{MaxAttempts})...");
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                return new HubReply((int)response.StatusCode, body, stopwatch.Elapsed);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Reads the state behind the graphical preview: points, units with positions, the human-found
    /// flag and the queue of pending animations. It is not an action, so looking costs nothing. The
    /// backend checks the request origin, which is why the call presents itself as the preview page.
    /// </summary>
    public async Task<HubReply> PullStateAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await WaitOutMinIntervalAsync(cancellationToken);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/domatowo_backend.php");
            request.Headers.Referrer = new Uri($"{_baseUrl}/domatowo_preview");
            request.Headers.TryAddWithoutValidation("Origin", _baseUrl);
            request.Headers.Accept.ParseAdd("application/json");
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["action"] = "pull", ["key"] = _apiKey });

            var stopwatch = Stopwatch.StartNew();
            using var response = await _http.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();

            StateReadsSent++;
            Log("backend", "pull", null, (int)response.StatusCode, body, stopwatch.Elapsed);

            return new HubReply((int)response.StatusCode, body, stopwatch.Elapsed);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsNeverApplied(HttpStatusCode status) =>
        status is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    private async Task WaitOutMinIntervalAsync(CancellationToken cancellationToken)
    {
        var wait = _lastRequestAt + _minInterval - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, cancellationToken);

        _lastRequestAt = DateTimeOffset.UtcNow;
    }

    private void Log(string kind, string action, JsonObject? answer, int status, string body, TimeSpan elapsed)
    {
        var entry = new JsonObject
        {
            ["at"] = DateTimeOffset.Now.ToString("O"),
            ["kind"] = kind,
            ["action"] = action,
            ["answer"] = answer?.DeepClone(),
            ["status"] = status,
            ["ms"] = (int)elapsed.TotalMilliseconds,
            ["body"] = Redact(body)
        };

        File.AppendAllText(_logPath, entry.ToJsonString(LogOptions) + Environment.NewLine);
    }

    private string Redact(string text) => _apiKey.Length > 0 ? text.Replace(_apiKey, "***") : text;

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
