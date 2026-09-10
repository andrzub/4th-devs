namespace _03_01_zadanie.Llm;

/// <summary>
/// Everything that can be sent in a single chat-completions request. There is no tool list
/// here: this task needs a batch classifier, not an agent, so the whole function-calling
/// machinery from the earlier episodes is deliberately absent.
/// </summary>
public class LlmRequest
{
    /// <summary>Model identifier. Empty means "use the client's default model".</summary>
    public string Model { get; set; } = string.Empty;

    public List<Message> Messages { get; set; } = new();

    /// <summary>Sampling temperature. Null = use provider default.</summary>
    public double? Temperature { get; set; }

    /// <summary>Maximum tokens to generate. Null = use provider default.</summary>
    public int? MaxTokens { get; set; }
}
