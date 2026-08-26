namespace ProxyServer.Llm;

/// <summary>
/// Encapsulates everything that can be sent in a single chat-completions request.
/// </summary>
public class LlmRequest
{
    /// <summary>Model identifier, e.g. "gpt-4.1". Empty = use the client's default.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Full conversation history. Caller owns and manages this list.</summary>
    public List<Message> Messages { get; set; } = [];

    /// <summary>Optional list of tool schemas the model may call.</summary>
    public List<ToolDefinition>? Tools { get; set; }

    /// <summary>Sampling temperature. Null = use provider default.</summary>
    public double? Temperature { get; set; }

    /// <summary>Maximum tokens to generate. Null = use provider default.</summary>
    public int? MaxTokens { get; set; }
}
