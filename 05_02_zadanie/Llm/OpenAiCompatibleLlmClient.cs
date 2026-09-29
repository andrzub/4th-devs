using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _05_02_zadanie.Llm;

/// <summary>
/// <see cref="ILlmClient"/> implementation for any provider speaking the OpenAI
/// chat-completions protocol. No third-party SDK — only HttpClient and System.Text.Json.
/// Requests are serialised and optionally spaced apart to respect per-minute rate limits.
/// </summary>
public class OpenAiCompatibleLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _providerName;
    private readonly string _defaultModel;
    private readonly TimeSpan _minRequestInterval;
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private DateTimeOffset _lastRequestStartedAt = DateTimeOffset.MinValue;

    public OpenAiCompatibleLlmClient(string providerName, LlmProviderSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException($"Missing API key for provider '{providerName}' — set it in appsettings.Development.json.");

        _providerName = providerName;
        _defaultModel = settings.DefaultModel;
        _minRequestInterval = TimeSpan.FromSeconds(settings.MinSecondsBetweenRequests);

        _http = new HttpClient { BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(2) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private const int MaxAttempts = 5;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var json = BuildRequestBody(request).ToJsonString();

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                await WaitOutMinIntervalAsync(cancellationToken);

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var httpResponse = await _http.PostAsync("chat/completions", content, cancellationToken);

                var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

                if (httpResponse.StatusCode is System.Net.HttpStatusCode.TooManyRequests or System.Net.HttpStatusCode.ServiceUnavailable && attempt < MaxAttempts)
                {
                    var delay = httpResponse.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5 * attempt);
                    Console.WriteLine($"  [{_providerName}] {(int)httpResponse.StatusCode}, retrying in {delay.TotalSeconds:F0}s (attempt {attempt}/{MaxAttempts})...");
                    await Task.Delay(delay, cancellationToken);
                    continue;
                }

                if (!httpResponse.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"{_providerName} API error {(int)httpResponse.StatusCode}: {responseBody}", null, httpResponse.StatusCode);
                }

                return ParseResponse(responseBody);
            }
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private async Task WaitOutMinIntervalAsync(CancellationToken cancellationToken)
    {
        if (_minRequestInterval <= TimeSpan.Zero)
            return;

        var earliestNextStart = _lastRequestStartedAt + _minRequestInterval;
        var wait = earliestNextStart - DateTimeOffset.UtcNow;
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, cancellationToken);

        _lastRequestStartedAt = DateTimeOffset.UtcNow;
    }

    // -------------------------------------------------------------------------
    // Request building
    // -------------------------------------------------------------------------

    private JsonObject BuildRequestBody(LlmRequest request)
    {
        var body = new JsonObject
        {
            ["model"] = request.Model.Length > 0 ? request.Model : _defaultModel,
            ["messages"] = BuildMessages(request.Messages)
        };

        if (request.Temperature.HasValue)
            body["temperature"] = request.Temperature.Value;

        if (request.MaxTokens.HasValue)
            body["max_tokens"] = request.MaxTokens.Value;

        if (request.ResponseFormat.HasValue)
            body["response_format"] = new JsonObject
            {
                ["type"]        = "json_schema",
                ["json_schema"] = JsonNode.Parse(request.ResponseFormat.Value.GetRawText())
            };

        return body;
    }

    private static JsonArray BuildMessages(IEnumerable<Message> messages)
    {
        var arr = new JsonArray();

        foreach (var msg in messages)
            arr.Add(new JsonObject { ["role"] = msg.RoleName, ["content"] = msg.Content });

        return arr;
    }

    // -------------------------------------------------------------------------
    // Response parsing
    // -------------------------------------------------------------------------

    private static LlmResponse ParseResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var choice = root.GetProperty("choices")[0];
        var message = choice.GetProperty("message");
        var finishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() ?? "" : "";

        string? content = null;
        if (message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            content = contentEl.GetString();

        LlmUsage usage = new();
        if (root.TryGetProperty("usage", out var usageEl))
        {
            usage = new LlmUsage
            {
                PromptTokens     = usageEl.TryGetProperty("prompt_tokens",     out var pt) ? pt.GetInt32() : 0,
                CompletionTokens = usageEl.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0,
                TotalTokens      = usageEl.TryGetProperty("total_tokens",      out var tt) ? tt.GetInt32() : 0
            };
        }

        return new LlmResponse
        {
            Content      = content,
            FinishReason = finishReason,
            Usage        = usage
        };
    }
}
