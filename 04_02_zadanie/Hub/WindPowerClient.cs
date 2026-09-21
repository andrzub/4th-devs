using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using _04_02_zadanie.Analysis;

namespace _04_02_zadanie.Hub;

public sealed record HubReply(int Status, string Body, TimeSpan Elapsed)
{
    public bool IsSuccess => Status is >= 200 and < 300;
}

/// <summary>
/// Every call this run makes to the windpower API. Unlike the earlier tasks the calls are
/// deliberately not serialised and not spaced apart: the service window lasts seconds and the
/// reports are queued, so several requests have to be in flight at once. Only the log file is
/// guarded, because that is the one shared resource.
/// </summary>
public sealed class WindPowerClient : IDisposable
{
    private const string TaskName = "windpower";

    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _logPath;
    private readonly Lock _logGate = new();
    private int _requestsSent;

    public WindPowerClient(string baseUrl, string apiKey, string logPath, int requestTimeoutSeconds)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _logPath = logPath;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(requestTimeoutSeconds) };
    }

    public int RequestsSent => Volatile.Read(ref _requestsSent);

    public Task<HubReply> HelpAsync(CancellationToken cancellationToken = default) =>
        PostAsync(new JsonObject { ["action"] = "help" }, cancellationToken);

    public Task<HubReply> StartAsync(CancellationToken cancellationToken = default) =>
        PostAsync(new JsonObject { ["action"] = "start" }, cancellationToken);

    public Task<HubReply> GetAsync(string param, CancellationToken cancellationToken = default) =>
        PostAsync(new JsonObject { ["action"] = "get", ["param"] = param }, cancellationToken);

    public Task<HubReply> GetResultAsync(CancellationToken cancellationToken = default) =>
        PostAsync(new JsonObject { ["action"] = "getResult" }, cancellationToken);

    public Task<HubReply> UnlockCodeAsync(ConfigPoint point, CancellationToken cancellationToken = default) =>
        PostAsync(new JsonObject
        {
            ["action"] = "unlockCodeGenerator",
            ["startDate"] = point.StartDate,
            ["startHour"] = point.StartHour,
            ["windMs"] = point.WindMs,
            ["pitchAngle"] = point.PitchAngle
        }, cancellationToken);

    /// <summary>
    /// Stores every point in one request. The batch form exists and the window has no room for a
    /// round trip per point, so a partially configured turbine is never a state this run can reach.
    /// </summary>
    public Task<HubReply> ConfigAsync(IEnumerable<ConfigPoint> points, IReadOnlyDictionary<string, string> signatures, CancellationToken cancellationToken = default)
    {
        var configs = new JsonObject();

        foreach (var point in points)
            configs[point.Key] = new JsonObject
            {
                ["pitchAngle"] = point.PitchAngle,
                ["turbineMode"] = point.TurbineMode,
                ["unlockCode"] = signatures.TryGetValue(point.Key, out var code) ? code : string.Empty
            };

        return PostAsync(new JsonObject { ["action"] = "config", ["configs"] = configs }, cancellationToken);
    }

    public Task<HubReply> DoneAsync(CancellationToken cancellationToken = default) =>
        PostAsync(new JsonObject { ["action"] = "done" }, cancellationToken);

    public async Task<HubReply> PostAsync(JsonObject answer, CancellationToken cancellationToken = default)
    {
        var payload = new JsonObject { ["apikey"] = _apiKey, ["task"] = TaskName, ["answer"] = answer.DeepClone() };
        using var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        var stopwatch = Stopwatch.StartNew();
        using var response = await _http.PostAsync($"{_baseUrl}/verify", content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();

        Interlocked.Increment(ref _requestsSent);
        Log(answer, (int)response.StatusCode, body, stopwatch.Elapsed);

        return new HubReply((int)response.StatusCode, body, stopwatch.Elapsed);
    }

    private void Log(JsonObject answer, int status, string body, TimeSpan elapsed)
    {
        var entry = new JsonObject
        {
            ["at"] = DateTimeOffset.Now.ToString("O"),
            ["ms"] = (int)elapsed.TotalMilliseconds,
            ["answer"] = answer.DeepClone(),
            ["status"] = status,
            ["body"] = Redact(body)
        };

        var line = entry.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        lock (_logGate)
            File.AppendAllText(_logPath, line + Environment.NewLine);
    }

    private string Redact(string text) => _apiKey.Length > 0 ? text.Replace(_apiKey, "***") : text;

    public void Dispose() => _http.Dispose();
}
