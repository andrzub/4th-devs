using System.Globalization;
using System.Text.RegularExpressions;

namespace _02_03_zadanie.Analysis;

/// <summary>
/// Parses the raw plant log text ("[YYYY-MM-DD HH:MM:SS] [LEVEL] message") into entries.
/// Lines that do not match are kept aside rather than dropped silently.
/// </summary>
public static partial class LogParser
{
    [GeneratedRegex(@"^\[(\d{4}-\d{2}-\d{2}) (\d{2}:\d{2}:\d{2})\] \[([A-Z]+)\] (.*)$")]
    private static partial Regex LineRegex();

    // Component identifiers are all-caps tokens, optionally with digits (ECCS8, WTANK07, FIRMWARE).
    [GeneratedRegex(@"\b[A-Z][A-Z0-9]{2,}\b")]
    private static partial Regex ComponentRegex();

    public static ParsedLog Parse(string text)
    {
        var entries = new List<LogEntry>();
        var unparsed = new List<string>();
        var lineNumber = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
                continue;

            var match = LineRegex().Match(line);
            if (!match.Success)
            {
                unparsed.Add(line);
                continue;
            }

            var message = match.Groups[4].Value;
            var components = ComponentRegex().Matches(message).Select(m => m.Value).Distinct().ToArray();

            entries.Add(new LogEntry(
                lineNumber,
                DateOnly.ParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                TimeOnly.ParseExact(match.Groups[2].Value, "HH:mm:ss", CultureInfo.InvariantCulture),
                match.Groups[3].Value,
                message,
                components));
        }

        return new ParsedLog(entries, unparsed);
    }
}

public sealed class ParsedLog
{
    public IReadOnlyList<LogEntry> Entries { get; }
    public IReadOnlyList<string> UnparsedLines { get; }

    /// <summary>All component identifiers seen in the log, most frequently mentioned first.</summary>
    public IReadOnlyList<string> Components { get; }

    public ParsedLog(IReadOnlyList<LogEntry> entries, IReadOnlyList<string> unparsedLines)
    {
        Entries = entries;
        UnparsedLines = unparsedLines;
        Components = entries.SelectMany(e => e.Components)
            .GroupBy(c => c)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToArray();
    }

    public IEnumerable<LogEntry> Where(LogFilter filter) => Entries.Where(filter.Matches);
}
