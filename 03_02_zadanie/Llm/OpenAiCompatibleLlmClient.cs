using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using _03_02_zadanie.Tools;

namespace _03_02_zadanie.Llm;

/// <summary>
/// <see cref="ILlmClient"/> implementation for any provider speaking the OpenAI
/// chat-completions protocol. Used both for OpenAI itself and for Gemini via its
/// OpenAI-compatible endpoint. No third-party SDK — only HttpClient and System.Text.Json.
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

        _http = new HttpClient { BaseAddress = new Uri(settings.BaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(5) };
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

    /// <summary>
    /// Gemini returns no Retry-After header — the suggested wait lives in the error body
    /// as <c>"retryDelay": "41s"</c> (google.rpc.RetryInfo). A small safety margin is added.
    /// </summary>
    private static TimeSpan? ParseRetryDelayFromBody(string responseBody)
    {
        var match = System.Text.RegularExpressions.Regex.Match(responseBody, "\"retryDelay\"\\s*:\\s*\"(\\d+(?:\\.\\d+)?)s\"");
        return match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(seconds + 2)
            : null;
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

        if (request.Tools is { Count: > 0 })
            body["tools"] = BuildTools(request.Tools);

        if (request.ResponseFormat.HasValue)
            body["response_format"] = BuildResponseFormat(request.ResponseFormat.Value);

        return body;
    }

    private static JsonArray BuildMessages(IEnumerable<Message> messages)
    {
        var arr = new JsonArray();

        foreach (var msg in messages)
        {
            var obj = new JsonObject { ["role"] = msg.RoleName };

            if (msg.ImageDataUrls is { Count: > 0 })
                obj["content"] = BuildContentParts(msg);
            else
                obj["content"] = msg.Content;

            if (msg.Role == MessageRole.Tool && msg.ToolCallId is not null)
                obj["tool_call_id"] = msg.ToolCallId;

            if (msg.Role == MessageRole.Assistant && msg.ToolCalls is { Count: > 0 })
            {
                var toolCalls = new JsonArray();
                foreach (var tc in msg.ToolCalls)
                {
                    toolCalls.Add(new JsonObject
                    {
                        ["id"]   = tc.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"]      = tc.FunctionName,
                            ["arguments"] = tc.ArgumentsJson
                        }
                    });
                }
                obj["tool_calls"] = toolCalls;
            }

            arr.Add(obj);
        }

        return arr;
    }

    /// <summary>
    /// Serialises a message carrying images. "detail": "high" is deliberate — the board
    /// tiles are small crops and the low-detail tier downsamples them past readability.
    /// Gemini's OpenAI-compatible endpoint accepts and ignores fields it does not support.
    /// </summary>
    private static JsonArray BuildContentParts(Message msg)
    {
        var parts = new JsonArray
        {
            new JsonObject { ["type"] = "text", ["text"] = msg.Content ?? string.Empty }
        };

        foreach (var dataUrl in msg.ImageDataUrls!)
        {
            parts.Add(new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = dataUrl, ["detail"] = "high" }
            });
        }

        return parts;
    }

    private static JsonArray BuildTools(IEnumerable<ITool> tools)
    {
        var arr = new JsonArray();

        foreach (var tool in tools)
        {
            arr.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"]        = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"]  = JsonNode.Parse(tool.ParametersSchema.GetRawText())
                }
            });
        }

        return arr;
    }

    private static JsonNode BuildResponseFormat(JsonElement schema)
    {
        return new JsonObject
        {
            ["type"]        = "json_schema",
            ["json_schema"] = JsonNode.Parse(schema.GetRawText())
        };
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

        string? contentRaw = null;
        if (message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            contentRaw = contentEl.GetString();

        var toolCalls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCallsEl) && toolCallsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in toolCallsEl.EnumerateArray())
            {
                var fn = tc.GetProperty("function");
                toolCalls.Add(new ToolCall
                {
                    Id            = tc.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    FunctionName  = fn.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    ArgumentsJson = fn.TryGetProperty("arguments", out var args) ? args.GetString() ?? "{}" : "{}"
                });
            }
        }

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
            ContentRaw   = contentRaw,
            Content      = toolCalls.Count == 0 ? contentRaw : null,
            ToolCalls    = toolCalls,
            FinishReason = finishReason,
            Usage        = usage
        };
    }
}
