using System.Text;
using System.Text.Json;
using _02_03_zadanie.Analysis;
using _02_03_zadanie.Llm;

namespace _02_03_zadanie.Tools;

/// <summary>
/// The subagent: every line mentioning one component goes to a cheap scanner model, which
/// returns a compact timeline. The main agent never sees the raw lines, so hundreds of them
/// cost a fraction of what the agent model would charge for reading them itself.
/// </summary>
public sealed class SummarizeComponentTool(ParsedLog log, OpenAiCompatibleLlmClient scanner) : ITool
{
    private const string ScannerInstructions =
        """
        You are a log analyst. You receive every line of a power plant's system log for one day that
        mentions one component. Produce a compact chronological timeline of that component's condition:
        - when each distinct symptom first appears (exact HH:MM and severity level), how often it repeats
          and when it stops,
        - escalations: the moment a condition becomes critical or permanent,
        - interactions with other components mentioned in the same lines, including cause and effect
          when the text states it,
        - the final state at the end of the log.
        Use only facts present in the lines; keep timestamps, levels and identifiers exact; do not speculate
        beyond what the text says. Output at most 25 lines of plain text, each starting with HH:MM.
        """;

    public string Name => "summarize_component";

    public string Description =>
        "Delegates the reading of every log line that mentions one component to a cheap scanner model and returns a " +
        "compact chronological timeline of that component's condition: first appearance of each symptom, escalations, " +
        "interactions with other components, final state. Use it to understand one component's story without " +
        "reading hundreds of lines yourself.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "component": {
              "type": "string",
              "description": "Component identifier exactly as it appears in the log."
            },
            "min_level": {
              "type": "string",
              "enum": ["INFO", "WARN", "ERRO", "CRIT"],
              "description": "Lowest severity to hand to the scanner. Default INFO (everything)."
            }
          },
          "required": ["component"]
        }
        """).RootElement;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = ToolArguments.Parse(argumentsJson);
        var component = args.GetString("component")?.Trim();
        if (string.IsNullOrEmpty(component))
            return "Missing 'component'. Known components: " + string.Join(", ", log.Components);

        var known = log.Components.FirstOrDefault(c => c.Equals(component, StringComparison.OrdinalIgnoreCase));
        if (known is null)
            return $"Unknown component '{component}'. Known components: {string.Join(", ", log.Components)}";

        var filter = new LogFilter { Component = known, MinLevel = args.GetString("min_level") ?? "INFO" };
        var lines = log.Where(filter).ToList();
        if (lines.Count == 0)
            return $"No lines mention {known} at level {filter.MinLevel} or above.";

        var byLevel = string.Join(", ", lines.GroupBy(e => e.Level).OrderByDescending(g => LogLevels.Rank(g.Key)).Select(g => $"{g.Key} {g.Count()}"));
        Console.WriteLine($"  [summarize_component] scanner ({scanner.ModelName}) reading {lines.Count} lines mentioning {known}...");

        var request = new LlmRequest
        {
            Messages =
            [
                Message.System(ScannerInstructions),
                Message.User($"Component: {known}\nLines ({lines.Count}):\n" + string.Join("\n", lines.Select(e => e.ToLine())))
            ],
            Temperature = 0
        };
        var response = await scanner.CompleteAsync(request, cancellationToken);

        var sb = new StringBuilder();
        sb.AppendLine($"Scanner ({scanner.ModelName}) read {lines.Count} lines mentioning {known} ({byLevel}); " +
                      $"{response.Usage.PromptTokens} prompt tokens stayed out of your context. Timeline:");
        sb.Append(response.Content?.Trim() ?? "(scanner returned no text)");
        return sb.ToString();
    }
}
