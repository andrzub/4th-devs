namespace _02_01_zadanie.Llm;

/// <summary>
/// Common interface for all LLM providers.
/// Implement this to add a new provider (Azure OpenAI, Anthropic, Gemini, Ollama, etc.).
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Sends a chat-completions request and returns the model's response.
    /// </summary>
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
