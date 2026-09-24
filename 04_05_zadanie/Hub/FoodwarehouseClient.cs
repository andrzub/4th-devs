using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using _04_05_zadanie.Warehouse;

namespace _04_05_zadanie.Hub;

/// <summary>
/// Every call this run makes to the foodwarehouse API. Calls are serialised, spaced apart, counted
/// and logged here rather than left to the model. Database queries pass the read-only guard before
/// they leave the process. Calls that change an order (create, append) are never repeated after a
/// transport error: an append that did reach the hub adds its quantity a second time.
/// </summary>
public sealed class FoodwarehouseClient(string baseUrl, string apiKey, string logPath, int maxRequests, double minSecondsBetweenRequests)
{
    private const int MaxAttempts = 4;
    private const string TaskName = "foodwarehouse";

    private static readonly JsonSerializerOptions RelaxedJson = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _baseUrl = baseUrl.TrimEnd('/');
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _minInterval = TimeSpan.FromSeconds(minSecondsBetweenRequests);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public int RequestsSent { get; private set; }

    public int RemainingRequests => maxRequests - RequestsSent;

    public Task<HubReply> HelpAsync(CancellationToken cancellationToken = default) =>
        SendAsync(Tool("help"), safeToRepeat: true, cancellationToken);

    /// <summary>Runs a read-only query. The guard's refusal is an exception here: tools explain it to the model themselves.</summary>
    public Task<HubReply> QueryAsync(string query, CancellationToken cancellationToken = default)
    {
        var verdict = QueryGuard.Evaluate(query);
        if (!verdict.Allowed)
            throw new ArgumentException($"Query refused by the guard: {verdict.Reason}");

        var answer = Tool("database");
        answer["query"] = verdict.Query;
        return SendAsync(answer, safeToRepeat: true, cancellationToken);
    }

    public Task<HubReply> GetOrdersAsync(string? id = null, CancellationToken cancellationToken = default)
    {
        var answer = OrdersAction("get");
        if (id is not null)
            answer["id"] = id;
        return SendAsync(answer, safeToRepeat: true, cancellationToken);
    }

    public Task<HubReply> CreateOrderAsync(string title, int creatorId, int destination, string signature, CancellationToken cancellationToken = default)
    {
        var answer = OrdersAction("create");
        answer["title"] = title;
        answer["creatorID"] = creatorId;
        answer["destination"] = destination;
        answer["signature"] = signature;
        return SendAsync(answer, safeToRepeat: false, cancellationToken);
    }

    /// <summary>Batch append: one call adds every item of the order.</summary>
    public Task<HubReply> AppendItemsAsync(string id, IReadOnlyDictionary<string, int> items, CancellationToken cancellationToken = default)
    {
        var batch = new JsonObject();
        foreach (var (name, quantity) in items)
            batch[name] = quantity;

        var answer = OrdersAction("append");
        answer["id"] = id;
        answer["items"] = batch;
        return SendAsync(answer, safeToRepeat: false, cancellationToken);
    }

    public Task<HubReply> DeleteOrderAsync(string id, CancellationToken cancellationToken = default)
    {
        var answer = OrdersAction("delete");
        answer["id"] = id;
        return SendAsync(answer, safeToRepeat: true, cancellationToken);
    }

    public Task<HubReply> GenerateSignatureAsync(string login, string birthday, int destination, CancellationToken cancellationToken = default)
    {
        var answer = Tool("signatureGenerator");
        answer["action"] = "generate";
        answer["login"] = login;
        answer["birthday"] = birthday;
        answer["destination"] = destination;
        return SendAsync(answer, safeToRepeat: true, cancellationToken);
    }

    public Task<HubReply> ResetAsync(CancellationToken cancellationToken = default) =>
        SendAsync(Tool("reset"), safeToRepeat: true, cancellationToken);

    public Task<HubReply> DoneAsync(CancellationToken cancellationToken = default) =>
        SendAsync(Tool("done"), safeToRepeat: true, cancellationToken);

    /// <summary>
    /// Sends one answer. 429 and 503 are retried for every call, because the hub did not process
    /// them. A transport failure (timeout, connection reset) is retried only when the call is safe
    /// to repeat; otherwise it surfaces as <see cref="HubTransportException"/>.
    /// </summary>
    public async Task<HubReply> SendAsync(JsonNode answer, bool safeToRepeat, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (RequestsSent >= maxRequests)
                throw new HubBudgetExceededException($"Request budget spent: {maxRequests} calls already made to the foodwarehouse API. The run stops here.");

            var payload = new JsonObject { ["apikey"] = apiKey, ["task"] = TaskName, ["answer"] = answer.DeepClone() };
            var json = payload.ToJsonString(RelaxedJson);

            for (var attempt = 1; ; attempt++)
            {
                await WaitOutMinIntervalAsync(cancellationToken);

                HttpResponseMessage response;
                string body;
                try
                {
                    using var content = new StringContent(json, Encoding.UTF8, "application/json");
                    response = await _http.PostAsync($"{_baseUrl}/verify", content, cancellationToken);
                    body = await response.Content.ReadAsStringAsync(cancellationToken);
                }
                catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
                {
                    RequestsSent++;
                    Log(answer, 0, $"transport error: {ex.Message}");

                    if (safeToRepeat && attempt < MaxAttempts)
                    {
                        Console.WriteLine($"  [hub] transport error, retrying (attempt {attempt}/{MaxAttempts}): {ex.Message}");
                        await Task.Delay(TimeSpan.FromSeconds(5 * attempt), cancellationToken);
                        continue;
                    }

                    throw new HubTransportException($"No reply from the hub for {Describe(answer)}: {ex.Message}. Whether it was processed is unknown.", ex);
                }

                using (response)
                {
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
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static JsonObject Tool(string tool) => new() { ["tool"] = tool };

    private static JsonObject OrdersAction(string action) => new() { ["tool"] = "orders", ["action"] = action };

    private static string Describe(JsonNode answer) =>
        answer is JsonObject obj ? string.Join(" ", new[] { obj["tool"]?.ToString(), obj["action"]?.ToString() }.Where(part => part is not null)) : answer.ToJsonString();

    private async Task WaitOutMinIntervalAsync(CancellationToken cancellationToken)
    {
        if (_minInterval <= TimeSpan.Zero)
            return;

        var wait = _lastRequestAt + _minInterval - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, cancellationToken);

        _lastRequestAt = DateTimeOffset.UtcNow;
    }

    private void Log(JsonNode answer, int status, string body)
    {
        var entry = new JsonObject
        {
            ["at"] = DateTimeOffset.Now.ToString("O"),
            ["answer"] = answer.DeepClone(),
            ["status"] = status,
            ["body"] = Redact(body)
        };

        File.AppendAllText(logPath, entry.ToJsonString(RelaxedJson) + Environment.NewLine);
    }

    private string Redact(string text) => apiKey.Length > 0 ? text.Replace(apiKey, "***") : text;
}
