using System.Text.Json;

namespace _01_02_zadanie.Tools;

/// <summary>
/// Contract for every tool that can be passed to an LLM.
/// Each implementation lives in its own class and carries both the logic
/// and the JSON schema definition needed by the API.
/// </summary>
public interface ITool
{
    /// <summary>Function name as it will appear in the API request (snake_case).</summary>
    string Name { get; }

    /// <summary>Human-readable description shown to the model.</summary>
    string Description { get; }

    /// <summary>
    /// JSON Schema object describing the function's parameters.
    /// Returned as a <see cref="JsonElement"/> so it can be serialised
    /// directly into the API payload without an extra round-trip.
    /// </summary>
    JsonElement ParametersSchema { get; }

    /// <summary>
    /// Executes the tool with the arguments provided by the model.
    /// </summary>
    /// <param name="argumentsJson">Raw JSON string of the arguments object.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A string result that will be sent back to the model as a tool message.</returns>
    Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default);
}
