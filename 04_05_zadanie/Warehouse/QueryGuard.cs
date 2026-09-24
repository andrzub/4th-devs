using System.Text.RegularExpressions;

namespace _04_05_zadanie.Warehouse;

public sealed record QueryVerdict(bool Allowed, string Reason, string Query);

/// <summary>
/// The read-only gate in front of the database tool. The hub blocks writes itself, but a refused
/// query still costs a request and a turn; here it costs neither. The allowed shapes are the ones
/// the API's help lists, plus the hub's own quirk: '&lt;' and '&gt;' anywhere in a query are rejected
/// as HTML tags, so the guard says so before the hub does.
/// </summary>
public static partial class QueryGuard
{
    [GeneratedRegex(@"^(\.tables|\.schema(\s+[A-Za-z_][A-Za-z0-9_]*)?|show\s+tables|show\s+create\s+table\s+[A-Za-z_][A-Za-z0-9_]*)$", RegexOptions.IgnoreCase)]
    private static partial Regex MetaCommand();

    [GeneratedRegex(@"^select(\s|\*|\()", RegexOptions.IgnoreCase)]
    private static partial Regex SelectStart();

    [GeneratedRegex(@"\b(insert|update|delete|drop|alter|create|replace|attach|detach|pragma|vacuum|reindex|into|with)\b", RegexOptions.IgnoreCase)]
    private static partial Regex WriteKeyword();

    public const string AllowedShapes = "SELECT ..., SHOW TABLES, SHOW CREATE TABLE <name>, .tables, .schema and .schema <name>";

    public static QueryVerdict Evaluate(string? query)
    {
        var trimmed = (query ?? string.Empty).Trim();

        if (trimmed.Length == 0)
            return Refuse(trimmed, "The query is empty.");

        if (trimmed.Contains('<') || trimmed.Contains('>'))
            return Refuse(trimmed, "The hub rejects '<' and '>' anywhere in a query (it treats them as HTML tags). Use != instead of <>, and BETWEEN, IN, IS NULL or ORDER BY ... LIMIT instead of range comparisons.");

        if (trimmed.Contains(';'))
            return Refuse(trimmed, "One statement per query; ';' is not allowed.");

        if (trimmed.Contains("--") || trimmed.Contains("/*"))
            return Refuse(trimmed, "Comments are not allowed in a query.");

        if (MetaCommand().IsMatch(trimmed))
            return new QueryVerdict(true, "ok", trimmed);

        if (!SelectStart().IsMatch(trimmed))
            return Refuse(trimmed, $"Only read-only queries are allowed: {AllowedShapes}.");

        if (WriteKeyword().Match(trimmed) is { Success: true } keyword)
            return Refuse(trimmed, $"'{keyword.Value.ToUpperInvariant()}' is not allowed in a query; the database is read-only and only plain SELECT statements pass.");

        return new QueryVerdict(true, "ok", trimmed);
    }

    private static QueryVerdict Refuse(string query, string reason) => new(false, reason, query);
}
