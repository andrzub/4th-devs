namespace _02_03_zadanie.Analysis;

/// <summary>
/// One parsed line of the plant log. The source format has no component field: identifiers
/// such as WTANK07 are embedded in the message text, so they are extracted from it.
/// A single line can mention several components.
/// </summary>
public sealed record LogEntry(int LineNumber, DateOnly Date, TimeOnly Time, string Level, string Message, IReadOnlyList<string> Components)
{
    public string ToLine() => $"[{Date:yyyy-MM-dd} {Time:HH:mm:ss}] [{Level}] {Message}";

    public bool Mentions(string component) => Components.Contains(component, StringComparer.OrdinalIgnoreCase);
}

public static class LogLevels
{
    private static readonly string[] BySeverity = ["INFO", "WARN", "ERRO", "CRIT"];

    /// <summary>Severity rank, higher is worse. Unknown levels rank below INFO.</summary>
    public static int Rank(string level) => Array.IndexOf(BySeverity, level.ToUpperInvariant());

    public static IReadOnlyList<string> Known => BySeverity;
}
