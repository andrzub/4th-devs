using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using _02_03_zadanie.Analysis;

namespace _02_03_zadanie.Tools;

/// <summary>
/// The "navigation" tool: grep over the parsed log. Results are capped so the model narrows
/// its filter instead of pulling hundreds of near-identical lines into its context.
/// </summary>
public sealed class SearchLogTool(ParsedLog log) : ITool
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    public string Name => "search_log";

    public string Description =>
        "Returns raw log lines matching the filter. All criteria are optional and combined with AND: severity levels, " +
        "component identifier, case-insensitive regular expression on the message text, time window. Use it to see " +
        "exact wording and timing. Results are capped (default 50, max 200); narrow the filter rather than paging " +
        "through hundreds of repeated lines.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "levels": {
              "type": "array",
              "items": { "type": "string", "enum": ["INFO", "WARN", "ERRO", "CRIT"] },
              "description": "Severity levels to include. Default: all."
            },
            "component": {
              "type": "string",
              "description": "Only lines mentioning this component identifier."
            },
            "pattern": {
              "type": "string",
              "description": "Case-insensitive regular expression matched against the message text."
            },
            "from": { "type": "string", "description": "Earliest time to include, HH:MM." },
            "to": { "type": "string", "description": "Latest time to include, HH:MM." },
            "limit": {
              "type": "integer",
              "description": "Maximum number of lines to return (default 50, max 200)."
            }
          }
        }
        """).RootElement;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = ToolArguments.Parse(argumentsJson);

        Regex? pattern = null;
        var patternText = args.GetString("pattern");
        if (!string.IsNullOrWhiteSpace(patternText))
        {
            try
            {
                pattern = new Regex(patternText, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2));
            }
            catch (ArgumentException ex)
            {
                return Task.FromResult($"Invalid regular expression '{patternText}': {ex.Message}");
            }
        }

        var filter = new LogFilter
        {
            Levels = args.GetStringArray("levels"),
            Component = args.GetString("component"),
            MessagePattern = pattern,
            From = args.GetTime("from"),
            To = args.GetTime("to")
        };
        var limit = Math.Clamp(args.GetInt("limit") ?? DefaultLimit, 1, MaxLimit);

        var matches = log.Where(filter).ToList();
        var shown = matches.Take(limit).ToList();

        var sb = new StringBuilder();
        sb.AppendLine(matches.Count > shown.Count
            ? $"{matches.Count} lines match; showing the first {shown.Count}. Narrow the filter or raise the limit (max {MaxLimit}) to see the rest."
            : $"{matches.Count} lines match.");
        foreach (var entry in shown)
            sb.AppendLine(entry.ToLine());

        return Task.FromResult(sb.ToString());
    }
}
