using System.Text.Json;
using _03_05_zadanie.Mission;

namespace _03_05_zadanie.Tools;

/// <summary>
/// The one endpoint known before the run starts. Its answer comes back raw: the descriptions,
/// scores and matched keywords are what tells the agent how the search reads a query, and a
/// paraphrase would hide exactly that.
/// </summary>
public sealed class SearchToolsTool(Expedition expedition) : ITool
{
    public string Name => "search_tools";

    public string Description =>
        "Searches the central tool registry and returns the tools that match, with their names, addresses and "
        + "descriptions. Accepts natural language or keywords, always in English. Returns only the few best "
        + "matches, never the whole registry, so different wordings can reveal different tools.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {"type":"object","properties":{"query":{"type":"string","description":"What you are looking for, in English."}},"required":["query"]}
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var query = ToolArguments.Parse(argumentsJson).GetString("query");
        return string.IsNullOrWhiteSpace(query)
            ? Task.FromResult("The search needs a query.")
            : expedition.SearchToolsAsync(query, cancellationToken);
    }
}
