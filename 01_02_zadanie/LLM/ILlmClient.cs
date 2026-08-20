namespace _01_02_zadanie.LLM;

/// <summary>
/// Common interface for all LLM providers.
/// Implement this to add a new provider (Azure OpenAI, Anthropic, Ollama, etc.).
/// </summary>
public interface ILlmClient
{
    /// <summary>
    /// Sends a chat-completions request and returns the model's response.
    /// </summary>
    /// <param name="request">The request including messages, optional tools and response format.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
