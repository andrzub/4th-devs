namespace _05_02_zadanie.Llm;

/// <summary>
/// The normalised response returned by any <see cref="ILlmClient"/> implementation.
/// </summary>
public class LlmResponse
{
    public string? Content { get; init; }

    /// <summary>Finish reason returned by the API (e.g. "stop", "length").</summary>
    public string FinishReason { get; init; } = string.Empty;

    public LlmUsage Usage { get; init; } = new();
}

public class LlmUsage
{
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public int TotalTokens { get; init; }
}
