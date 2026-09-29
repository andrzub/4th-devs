using System.Text.Json;

namespace _05_02_zadanie.Llm;

/// <summary>
/// Everything that can be sent in a single chat-completions request. There is no tool list here:
/// the call is scripted in code and the model only reads the operator's replies into a fixed shape,
/// so the function-calling machinery from the agent episodes is deliberately absent.
/// </summary>
public class LlmRequest
{
    /// <summary>Model identifier. Empty means "use the client's default model".</summary>
    public string Model { get; set; } = string.Empty;

    public List<Message> Messages { get; set; } = new();

    /// <summary>
    /// Optional JSON Schema for structured output.
    /// When set, the request includes <c>response_format: { type: "json_schema", json_schema: ... }</c>.
    /// </summary>
    public JsonElement? ResponseFormat { get; set; }

    /// <summary>Sampling temperature. Null = use provider default.</summary>
    public double? Temperature { get; set; }

    /// <summary>Maximum tokens to generate. Null = use provider default.</summary>
    public int? MaxTokens { get; set; }
}
