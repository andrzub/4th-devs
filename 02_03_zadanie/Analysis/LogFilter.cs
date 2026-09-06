using System.Text.RegularExpressions;

namespace _02_03_zadanie.Analysis;

/// <summary>
/// Query over parsed entries. Every criterion is optional; the set ones are combined with AND.
/// </summary>
public sealed class LogFilter
{
    /// <summary>Exact level names to keep (INFO/WARN/ERRO/CRIT).</summary>
    public IReadOnlyCollection<string>? Levels { get; init; }

    /// <summary>Lowest severity to keep, e.g. "WARN" keeps WARN, ERRO and CRIT.</summary>
    public string? MinLevel { get; init; }

    public string? Component { get; init; }

    public Regex? MessagePattern { get; init; }

    public TimeOnly? From { get; init; }

    public TimeOnly? To { get; init; }

    public bool Matches(LogEntry entry)
    {
        if (Levels is { Count: > 0 } && !Levels.Contains(entry.Level, StringComparer.OrdinalIgnoreCase))
            return false;
        if (MinLevel is not null && LogLevels.Rank(entry.Level) < LogLevels.Rank(MinLevel))
            return false;
        if (Component is not null && !entry.Mentions(Component))
            return false;
        if (MessagePattern is not null && !MessagePattern.IsMatch(entry.Message))
            return false;
        if (From is not null && entry.Time < From)
            return false;
        if (To is not null && entry.Time > To)
            return false;
        return true;
    }
}
