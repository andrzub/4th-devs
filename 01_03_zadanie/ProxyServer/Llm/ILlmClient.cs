namespace ProxyServer.Llm;

/// <summary>
/// Common interface for all LLM providers.
/// </summary>
public interface ILlmClient
{
    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}
