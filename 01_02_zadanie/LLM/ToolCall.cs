namespace _01_02_zadanie.LLM;

/// <summary>
/// Represents a single tool/function call requested by the model.
/// </summary>
public class ToolCall
{
    /// <summary>Unique ID assigned by the model (used to match the tool result back).</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Name of the function to invoke.</summary>
    public string FunctionName { get; init; } = string.Empty;

    /// <summary>Raw JSON string of the arguments object.</summary>
    public string ArgumentsJson { get; init; } = "{}";
}
