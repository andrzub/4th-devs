using System.Text.Json;
using _03_05_zadanie.Mission;

namespace _03_05_zadanie.Tools;

/// <summary>
/// Calls a tool the search has already returned. Each of them takes the same single parameter and
/// answers in JSON, so one generic call covers the whole toolbox — and a name that never appeared in
/// a search result is refused here instead of spending a request on a guess.
/// </summary>
public sealed class AskToolTool(Expedition expedition) : ITool
{
    public string Name => "ask_tool";

    public string Description =>
        "Sends a query to one of the tools found through search_tools and returns its raw answer. Every tool takes "
        + "the same single query parameter, but each expects a different kind of value, and an unsuitable one is "
        + "answered with an error that says what the tool wanted. Queries are always in English.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {"type":"object","properties":{
          "tool":{"type":"string","description":"Name of a tool returned earlier by search_tools."},
          "query":{"type":"string","description":"The value to send, in English."}},
         "required":["tool","query"]}
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var arguments = ToolArguments.Parse(argumentsJson);
        var tool = arguments.GetString("tool");
        var query = arguments.GetString("query");

        if (string.IsNullOrWhiteSpace(tool))
            return Task.FromResult("Name the tool to ask.");

        return string.IsNullOrWhiteSpace(query)
            ? Task.FromResult("The tool needs a query.")
            : expedition.AskToolAsync(tool, query, cancellationToken);
    }
}
