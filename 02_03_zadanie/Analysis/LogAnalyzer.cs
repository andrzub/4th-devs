using System.Text;

namespace _02_03_zadanie.Analysis;

/// <summary>
/// An identical message text repeated over time, collapsed into one row with its time span.
/// </summary>
public sealed record EventType(string Level, string Message, int Count, DateOnly FirstDate, TimeOnly FirstTime, TimeOnly LastTime, IReadOnlyList<string> Components)
{
    public string TimeSpanText => Count == 1 ? $"{FirstTime:HH:mm}" : $"{FirstTime:HH:mm}..{LastTime:HH:mm}";
}

/// <summary>
/// Bird's-eye views of the log: aggregate statistics and identical messages collapsed into
/// event types. The log is extremely repetitive, so these views are the cheap way to see
/// the whole day without reading thousands of lines.
/// </summary>
public static class LogAnalyzer
{
    public static IReadOnlyList<EventType> GroupByMessage(IEnumerable<LogEntry> entries) =>
        entries.GroupBy(e => (e.Level, e.Message))
            .Select(g =>
            {
                var ordered = g.OrderBy(e => e.Date).ThenBy(e => e.Time).ToList();
                return new EventType(g.Key.Level, g.Key.Message, ordered.Count, ordered[0].Date, ordered[0].Time, ordered[^1].Time, ordered[0].Components);
            })
            .OrderBy(t => t.FirstDate).ThenBy(t => t.FirstTime)
            .ToList();

    public static string RenderOverview(ParsedLog log, TokenCounter tokens)
    {
        var entries = log.Entries;
        var sb = new StringBuilder();
        sb.AppendLine("PLANT LOG OVERVIEW");
        sb.AppendLine($"Lines: {entries.Count} parsed, {log.UnparsedLines.Count} unparsed. Whole log: {tokens.Count(Join(entries))} tokens (o200k_base).");
        if (entries.Count > 0)
            sb.AppendLine($"Time range: {entries[0].Date:yyyy-MM-dd} {entries[0].Time:HH:mm:ss} .. {entries[^1].Date:yyyy-MM-dd} {entries[^1].Time:HH:mm:ss}");

        sb.AppendLine("Levels (lines / tokens / distinct message texts):");
        foreach (var level in entries.Select(e => e.Level).Distinct().OrderByDescending(LogLevels.Rank))
        {
            var ofLevel = entries.Where(e => e.Level == level).ToList();
            sb.AppendLine($"  {level}: {ofLevel.Count} / {tokens.Count(Join(ofLevel))} / {ofLevel.Select(e => e.Message).Distinct().Count()}");
        }

        sb.AppendLine("Components (lines mentioning them, by level):");
        foreach (var component in log.Components)
        {
            var mentioning = entries.Where(e => e.Mentions(component)).ToList();
            var byLevel = string.Join(", ", mentioning.GroupBy(e => e.Level).OrderByDescending(g => LogLevels.Rank(g.Key)).Select(g => $"{g.Key} {g.Count()}"));
            sb.AppendLine($"  {component}: {mentioning.Count} ({byLevel})");
        }

        var warnAndAbove = log.Where(new LogFilter { MinLevel = "WARN" }).ToList();
        var eventTypes = GroupByMessage(warnAndAbove);
        var onePerType = string.Join("\n", eventTypes.Select(t => $"[{t.FirstDate:yyyy-MM-dd} {t.FirstTime:HH:mm}] [{t.Level}] {t.Message}"));
        sb.AppendLine($"WARN and above: {warnAndAbove.Count} lines = {tokens.Count(Join(warnAndAbove))} tokens. " +
                      $"Only {eventTypes.Count} distinct message texts among them = {tokens.Count(onePerType)} tokens if each is kept once, unshortened, with its first timestamp.");
        return sb.ToString();
    }

    public static string RenderEventTypes(IEnumerable<EventType> eventTypes)
    {
        var sb = new StringBuilder();
        foreach (var t in eventTypes)
            sb.AppendLine($"{t.Count,4}x [{t.Level}] {t.TimeSpanText,-12} {t.Message}");
        return sb.ToString();
    }

    private static string Join(IEnumerable<LogEntry> entries) => string.Join("\n", entries.Select(e => e.ToLine()));
}
