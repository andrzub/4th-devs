using System.Text.Json;
using _02_03_zadanie.Analysis;

namespace _02_03_zadanie.Tools;

/// <summary>
/// The "perspective" tool: aggregate statistics of the whole log. Computed once and cached,
/// since token-counting the full file takes a moment.
/// </summary>
public sealed class LogOverviewTool(ParsedLog log, TokenCounter tokens) : ITool
{
    private readonly Lazy<string> _overview = new(() => LogAnalyzer.RenderOverview(log, tokens));

    public string Name => "log_overview";

    public string Description =>
        "Bird's-eye view of the full plant log: size in lines and tokens, time range, line and token counts " +
        "per severity level, every component identifier with its counts per level, and how much the log repeats " +
        "itself. Free and instant; call it first.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        { "type": "object", "properties": {} }
        """).RootElement;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default) =>
        Task.FromResult(_overview.Value);
}
