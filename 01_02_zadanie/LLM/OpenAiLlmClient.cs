using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using _01_02_zadanie.Tools;

namespace _01_02_zadanie.LLM;

/// <summary>
/// OpenAI implementation of <see cref="ILlmClient"/> using raw HTTP calls.
/// No third-party OpenAI SDK — only <see cref="HttpClient"/> and System.Text.Json.
/// </summary>
public class OpenAiLlmClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _defaultModel;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public OpenAiLlmClient(string apiKey, string baseUrl = "https://api.openai.com/v1", string defaultModel = "gpt-4o-mini")
    {
        _defaultModel = defaultModel;

        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private const int MaxAttempts = 5;

    public async Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default)
    {
        var body = BuildRequestBody(request);
        var json = body.ToJsonString();

        for (var attempt = 1; ; attempt++)
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var httpResponse = await _http.PostAsync("chat/completions", content, cancellationToken);

            var responseBody = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

            if (httpResponse.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < MaxAttempts)
            {
                var delay = httpResponse.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5 * attempt);
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
            var obj = new JsonObject
            {
                ["role"]    = msg.RoleName,
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
        // Caller passes the full json_schema object.
        // Wrap it as: { "type": "json_schema", "json_schema": <caller-supplied> }
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

        // Content
        string? contentRaw = null;
        if (message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            contentRaw = contentEl.GetString();

        // Tool calls
        var toolCalls = new List<ToolCall>();
        if (message.TryGetProperty("tool_calls", out var toolCallsEl) && toolCallsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var tc in toolCallsEl.EnumerateArray())
            {
                var fn = tc.GetProperty("function");
                toolCalls.Add(new ToolCall
                {
                    Id           = tc.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                    FunctionName = fn.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    ArgumentsJson = fn.TryGetProperty("arguments", out var args) ? args.GetString() ?? "{}" : "{}"
                });
            }
        }

        // Usage
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
