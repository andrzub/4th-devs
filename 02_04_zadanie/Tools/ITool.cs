using System.Text.Json;

namespace _02_04_zadanie.Tools;

/// <summary>
/// Contract for every tool that can be passed to an LLM.
/// Each implementation carries both the logic and the JSON schema definition needed by the API.
/// </summary>
public interface ITool
{
    /// <summary>Function name as it will appear in the API request (snake_case).</summary>
    string Name { get; }

    /// <summary>Human-readable description shown to the model.</summary>
    string Description { get; }

    /// <summary>JSON Schema object describing the function's parameters.</summary>
    JsonElement ParametersSchema { get; }

    /// <summary>
    /// Whether the agent loop may run this tool at the same time as the other calls in the same
    /// turn. Delegating work to several researchers in parallel is the point of this episode;
    /// anything that submits an answer must not race, so it opts out.
    /// </summary>
    bool IsParallelSafe => true;

    /// <summary>
    /// Executes the tool with the arguments provided by the model.
    /// </summary>
    /// <param name="argumentsJson">Raw JSON string of the arguments object.</param>
    /// <returns>A string result that will be sent back to the model as a tool message.</returns>
    Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default);
}
