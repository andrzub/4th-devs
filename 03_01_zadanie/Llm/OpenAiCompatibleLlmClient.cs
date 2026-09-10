using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _03_01_zadanie.Llm;

/// <summary>
/// <see cref="ILlmClient"/> implementation for any provider speaking the OpenAI
/// chat-completions protocol. No third-party SDK — only HttpClient and System.Text.Json.
/// Requests may run concurrently; the optional minimum interval serialises them instead.
/// </summary>
public class OpenAiCompatibleLlmClient : ILlmClient
{
    private const int MaxAttempts = 5;

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

        _http = new HttpClient { BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(5) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var json = BuildRequestBody(request).ToJsonString();

        for (var attempt = 1; ; attempt++)
        {
            await WaitOutMinIntervalAsync(cancellationToken);

            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var httpResponse = await _http.PostAsync("chat/completions", content, cancellationToken);

            var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (httpResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < MaxAttempts)
            {
                var delay = httpResponse.Headers.RetryAfter?.Delta
                    ?? ParseRetryDelayFromBody(responseBody)
                    ?? TimeSpan.FromSeconds(10 * attempt);
                Console.WriteLine($"  [{_providerName}] 429 rate limited, retrying in {delay.TotalSeconds:F0}s (attempt {attempt}/{MaxAttempts})...");
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            if (!httpResponse.IsSuccessStatusCode)
                throw new HttpRequestException($"{_providerName} API error {(int)httpResponse.StatusCode}: {responseBody}", null, httpResponse.StatusCode);

            return ParseResponse(responseBody);
        }
    }

    /// <summary>
    /// Providers that send no Retry-After header put the suggested wait in the error body
    /// as <c>"retryDelay": "41s"</c> (google.rpc.RetryInfo). A small safety margin is added.
    /// </summary>
    private static TimeSpan? ParseRetryDelayFromBody(string responseBody)
    {
        var match = System.Text.RegularExpressions.Regex.Match(responseBody, @"""retryDelay""\s*:\s*""(\d+(?:\.\d+)?)s""");
        return match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds + 2)
            : null;
    }

    /// <summary>
    /// Holds back the next request until the configured spacing has elapsed. With spacing set
    /// to zero the gate is never taken, which is what lets batches run in parallel.
    /// </summary>
    private async Task WaitOutMinIntervalAsync(CancellationToken cancellationToken)
    {
        if (_minRequestInterval <= TimeSpan.Zero)
            return;

        await _requestGate.WaitAsync(cancellationToken);
        try
        {
            var wait = _lastRequestStartedAt + _minRequestInterval - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken);

            _lastRequestStartedAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            _requestGate.Release();
        }
    }

    private JsonObject BuildRequestBody(LlmRequest request)
    {
        var messages = new JsonArray();
        foreach (var msg in request.Messages)
            messages.Add(new JsonObject { ["role"] = msg.RoleName, ["content"] = msg.Content });

        var body = new JsonObject
        {
            ["model"] = request.Model.Length > 0 ? request.Model : _defaultModel,
            ["messages"] = messages
        };

        if (request.Temperature.HasValue)
            body["temperature"] = request.Temperature.Value;

        if (request.MaxTokens.HasValue)
            body["max_tokens"] = request.MaxTokens.Value;

        return body;
    }

    private static LlmResponse ParseResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var choice = root.GetProperty("choices")[0];
        var message = choice.GetProperty("message");

        string? content = null;
        if (message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            content = contentEl.GetString();

        LlmUsage usage = new();
        if (root.TryGetProperty("usage", out var usageEl))
        {
            var cached = 0;
            if (usageEl.TryGetProperty("prompt_tokens_details", out var details) && details.TryGetProperty("cached_tokens", out var ct))
                cached = ct.GetInt32();

            usage = new LlmUsage
            {
                PromptTokens       = usageEl.TryGetProperty("prompt_tokens",     out var pt) ? pt.GetInt32() : 0,
                CachedPromptTokens = cached,
                CompletionTokens   = usageEl.TryGetProperty("completion_tokens", out var cp) ? cp.GetInt32() : 0,
                TotalTokens        = usageEl.TryGetProperty("total_tokens",      out var tt) ? tt.GetInt32() : 0
            };
        }

        return new LlmResponse
        {
            Content = content,
            FinishReason = choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() ?? "" : "",
            Usage = usage
        };
    }
}
