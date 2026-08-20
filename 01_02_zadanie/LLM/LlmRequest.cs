using System.Text.Json;
using _01_02_zadanie.Tools;

namespace _01_02_zadanie.LLM;

/// <summary>
/// Encapsulates everything that can be sent in a single chat-completions request.
/// </summary>
public class LlmRequest
{
    /// <summary>Model identifier, e.g. "gpt-4o-mini".</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Full conversation history. Caller owns and manages this list.
    /// Append <see cref="Message.System"/>, <see cref="Message.User"/>, etc. before passing.
    /// </summary>
    public List<Message> Messages { get; set; } = new();

    /// <summary>
    /// Optional list of tools the model may call.
    /// Each tool exposes its own schema via <see cref="ITool.GetDefinition"/>.
    /// </summary>
    public List<ITool>? Tools { get; set; }

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
