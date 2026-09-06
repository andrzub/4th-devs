using System.Text;
using System.Text.Json;
using _02_03_zadanie.Analysis;

namespace _02_03_zadanie.Tools;

/// <summary>
/// The collapsed view: identical message texts become one row with occurrence count and time
/// span. This is where most of the compression happens, deterministically and for free.
/// </summary>
public sealed class ListEventTypesTool(ParsedLog log) : ITool
{
    public string Name => "list_event_types";

    public string Description =>
        "Collapses identical message texts into event types and lists them chronologically by first occurrence: " +
        "how many times each repeated, when it first and last appeared, and its full text. Most of the log is " +
        "repetition, so this is the cheapest way to see every distinct thing that happened. Optional filters: " +
        "minimum severity (default WARN), component identifier, time window.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "min_level": {
              "type": "string",
              "enum": ["INFO", "WARN", "ERRO", "CRIT"],
              "description": "Lowest severity to include. Default WARN (WARN, ERRO and CRIT)."
            },
            "component": {
              "type": "string",
              "description": "Only event types whose text mentions this component identifier."
            },
            "from": { "type": "string", "description": "Earliest time to include, HH:MM." },
            "to": { "type": "string", "description": "Latest time to include, HH:MM." }
          }
        }
        """).RootElement;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = ToolArguments.Parse(argumentsJson);
        var filter = new LogFilter
        {
            MinLevel = args.GetString("min_level") ?? "WARN",
            Component = args.GetString("component"),
            From = args.GetTime("from"),
            To = args.GetTime("to")
        };

        var entries = log.Where(filter).ToList();
        var eventTypes = LogAnalyzer.GroupByMessage(entries);

        var sb = new StringBuilder();
        sb.AppendLine($"{eventTypes.Count} event types collapsed from {entries.Count} lines (min level {filter.MinLevel}" +
                      (filter.Component is null ? "" : $", component {filter.Component}") +
                      (filter.From is null && filter.To is null ? "" : $", window {filter.From:HH:mm}-{filter.To:HH:mm}") + ").");
        if (eventTypes.Count > 0)
        {
            var dates = string.Join(", ", eventTypes.Select(t => t.FirstDate).Distinct().Select(d => d.ToString("yyyy-MM-dd")));
            sb.AppendLine($"Date: {dates}. Row format: <occurrences>x [LEVEL] <first>..<last> <message>.");
        }
        sb.Append(LogAnalyzer.RenderEventTypes(eventTypes));
        return Task.FromResult(sb.ToString());
    }
}
