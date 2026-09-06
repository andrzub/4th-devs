using System.Globalization;
using System.Text.RegularExpressions;
using _02_03_zadanie.Analysis;

namespace _02_03_zadanie.Mission;

/// <summary>
/// Deterministic checks of a digest before it costs a submission: line format, that every
/// line's date, minute, level and component agree with a real source entry, and that concrete
/// key=value markers of that entry survived the paraphrase. Paraphrasing the message is
/// allowed; inventing events or dropping the specific facts is not.
/// </summary>
public sealed partial class DigestValidator
{
    [GeneratedRegex(@"^\[(\d{4}-\d{2}-\d{2}) (\d{1,2}:\d{2})\] \[([A-Za-z]+)\] (.+)$")]
    private static partial Regex DigestLineRegex();

    // Concrete markers such as SAFETY_CHECK=pass: the kind of detail a paraphrase tends to lose.
    [GeneratedRegex(@"[A-Za-z_][A-Za-z0-9_]*=[^\s.,;]+")]
    private static partial Regex MarkerRegex();

    private readonly ParsedLog _log;
    private readonly ILookup<(DateOnly Date, TimeOnly Minute, string Level), LogEntry> _sourcesByMinute;

    public DigestValidator(ParsedLog log)
    {
        _log = log;
        _sourcesByMinute = log.Entries.ToLookup(e => (e.Date, new TimeOnly(e.Time.Hour, e.Time.Minute), e.Level));
    }

    public IReadOnlyList<string> Validate(string digest)
    {
        var problems = new List<string>();
        var lines = digest.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var number = i + 1;

            if (line.Trim().Length == 0)
            {
                problems.Add($"line {number}: empty line");
                continue;
            }

            var match = DigestLineRegex().Match(line);
            if (!match.Success)
            {
                problems.Add($"line {number}: expected '[YYYY-MM-DD HH:MM] [LEVEL] COMPONENT text', got: {Truncate(line)}");
                continue;
            }

            if (!DateOnly.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                || !TimeOnly.TryParseExact(match.Groups[2].Value, ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var minute))
            {
                problems.Add($"line {number}: unreadable timestamp '{match.Groups[1].Value} {match.Groups[2].Value}'");
                continue;
            }

            var level = match.Groups[3].Value.ToUpperInvariant();
            if (LogLevels.Rank(level) < 0)
            {
                problems.Add($"line {number}: unknown level '{match.Groups[3].Value}' (use {string.Join("/", LogLevels.Known)})");
                continue;
            }

            var text = match.Groups[4].Value;
            var components = _log.Components.Where(c => text.Contains(c, StringComparison.Ordinal)).ToList();
            if (components.Count == 0)
            {
                problems.Add($"line {number}: no component identifier in the text (known: {string.Join(", ", _log.Components)})");
                continue;
            }

            var sources = _sourcesByMinute[(date, minute, level)].Where(source => components.Any(source.Mentions)).ToList();
            if (sources.Count == 0)
            {
                problems.Add($"line {number}: no source entry with level {level} at {date:yyyy-MM-dd} {minute:HH:mm} mentioning {string.Join("/", components)}; keep timestamps, levels and identifiers exactly as in the source");
                continue;
            }

            // Several source entries can share a minute; the line is fine if any of them has no
            // marker missing from it. Otherwise report the markers of the first candidate.
            var droppedMarkersPerSource = sources
                .Select(source => MarkerRegex().Matches(source.Message).Select(m => m.Value).Where(marker => !text.Contains(marker, StringComparison.Ordinal)).ToList())
                .ToList();
            if (droppedMarkersPerSource.All(dropped => dropped.Count > 0))
                problems.Add($"line {number}: the source entry contains '{string.Join("', '", droppedMarkersPerSource[0])}' but the digest line dropped it; keep concrete markers and values verbatim, they are what the technicians look for");
        }

        return problems;
    }

    private static string Truncate(string text) => text.Length <= 80 ? text : text[..77] + "...";
}
