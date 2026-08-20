using System.Text.Json;

namespace _01_02_zadanie.LLM;

/// <summary>
/// The normalised response returned by any <see cref="ILlmClient"/> implementation.
/// </summary>
public class LlmResponse
{
    /// <summary>
    /// Text content of the assistant message.
    /// Null when the model chose to call a tool instead of generating text,
    /// or when structured output is requested (use <see cref="ContentRaw"/> to get the JSON string).
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// Raw content string exactly as returned by the API (useful for structured output).
    /// </summary>
    public string? ContentRaw { get; init; }

    /// <summary>
    /// Tool calls requested by the model. Empty when the model did not call any tools.
    /// </summary>
    public IReadOnlyList<ToolCall> ToolCalls { get; init; } = Array.Empty<ToolCall>();

    /// <summary>Whether the model issued at least one tool call.</summary>
    public bool HasToolCalls => ToolCalls.Count > 0;

    /// <summary>Finish reason returned by the API (e.g. "stop", "tool_calls", "length").</summary>
    public string FinishReason { get; init; } = string.Empty;

    /// <summary>Token usage reported by the API.</summary>
    public LlmUsage Usage { get; init; } = new();
}

public class LlmUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
}
