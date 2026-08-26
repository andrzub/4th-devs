using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ProxyServer.Llm;

/// <summary>
/// OpenAI implementation of <see cref="ILlmClient"/> using raw HTTP calls.
/// No third-party OpenAI SDK — only <see cref="HttpClient"/> and System.Text.Json.
/// </summary>
public sealed class OpenAiLlmClient : ILlmClient
{
    private const int MaxAttempts = 4;

    private readonly HttpClient _http;
    private readonly string _defaultModel;

    public OpenAiLlmClient(string apiKey, string baseUrl = "https://api.openai.com/v1", string defaultModel = "gpt-4.1")
    {
        _defaultModel = defaultModel;

        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromSeconds(90) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var json = BuildRequestBody(request).ToJsonString();

        for (var attempt = 1; ; attempt++)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var httpResponse = await _http.PostAsync("chat/completions", content, cancellationToken);

            var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            var isRetryable = httpResponse.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;
            if (isRetryable && attempt < MaxAttempts)
            {
                var delay = httpResponse.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * attempt);
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            if (!httpResponse.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"OpenAI API error {(int)httpResponse.StatusCode}: {responseBody}",
                    null,
                    httpResponse.StatusCode);
            }

            return ParseResponse(responseBody);
        }
    }

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

        return body;
    }

    private static JsonArray BuildMessages(IEnumerable<Message> messages)
    {
        var arr = new JsonArray();

        foreach (var msg in messages)
        {
            var obj = new JsonObject
            {
                ["role"] = msg.RoleName,
                ["content"] = msg.Content
            };

            if (msg.Role == MessageRole.Tool && msg.ToolCallId is not null)
                obj["tool_call_id"] = msg.ToolCallId;

            if (msg.Role == MessageRole.Assistant && msg.ToolCalls is { Count: > 0 })
            {
                var toolCalls = new JsonArray();
                foreach (var tc in msg.ToolCalls)
                {
                    toolCalls.Add(new JsonObject
                    {
                        ["id"] = tc.Id,
                        ["type"] = "function",
                        ["function"] = new JsonObject
                        {
                            ["name"] = tc.FunctionName,
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

    private static JsonArray BuildTools(IEnumerable<ToolDefinition> tools)
    {
        var arr = new JsonArray();

        foreach (var tool in tools)
        {
            arr.Add(new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = tool.Name,
                    ["description"] = tool.Description,
                    ["parameters"] = JsonNode.Parse(tool.ParametersSchema.GetRawText())
                }
            });
        }

        return arr;
    }

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
                    Id = tc.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    FunctionName = fn.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    ArgumentsJson = fn.TryGetProperty("arguments", out var args) ? args.GetString() ?? "{}" : "{}"
                });
            }
        }

        LlmUsage usage = new();
        if (root.TryGetProperty("usage", out var usageEl))
        {
            usage = new LlmUsage
            {
                PromptTokens = usageEl.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0,
                CompletionTokens = usageEl.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0,
                TotalTokens = usageEl.TryGetProperty("total_tokens", out var tt) ? tt.GetInt32() : 0
            };
        }

        return new LlmResponse
        {
            ContentRaw = contentRaw,
            Content = toolCalls.Count == 0 ? contentRaw : null,
            ToolCalls = toolCalls,
            FinishReason = finishReason,
            Usage = usage
        };
    }
}
