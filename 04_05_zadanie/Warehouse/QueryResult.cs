using System.Text;
using System.Text.Json;

namespace _04_05_zadanie.Warehouse;

public sealed record SchemaEntry(string Table, string Schema);

/// <summary>
/// One reply of the database tool, whichever shape it took: rows of a SELECT or the entries of
/// .schema. The hub pages rows in blocks of thirty and reports the size of the whole table, not
/// of the filtered set, so a full page is flagged as possibly continued rather than declared complete.
/// </summary>
public sealed class QueryResult
{
    public string? Table { get; init; }

    public IReadOnlyList<string> Columns { get; init; } = [];

    public IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Rows { get; init; } = [];

    public IReadOnlyList<SchemaEntry> Schemas { get; init; } = [];

    public int Count { get; init; }

    public int? TotalTableRows { get; init; }

    public int? Limit { get; init; }

    public bool MayBeTruncated => Limit is { } limit && limit > 0 && Count >= limit;

    public static QueryResult Parse(string body)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("The database reply is not a JSON object.");

        var columns = root.TryGetProperty("columns", out var columnsElement) && columnsElement.ValueKind == JsonValueKind.Array
            ? columnsElement.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => c.GetString()!).ToList()
            : [];

        var rows = new List<IReadOnlyDictionary<string, JsonElement>>();
        if (root.TryGetProperty("rows", out var rowsElement) && rowsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rowsElement.EnumerateArray().Where(r => r.ValueKind == JsonValueKind.Object))
                rows.Add(row.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()));
        }

        var schemas = new List<SchemaEntry>();
        if (root.TryGetProperty("schemas", out var schemasElement) && schemasElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in schemasElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
            {
                var table = entry.TryGetProperty("table", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString()! : "?";
                var schema = entry.TryGetProperty("schema", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString()! : string.Empty;
                schemas.Add(new SchemaEntry(table, schema));
            }
        }

        return new QueryResult
        {
            Table = root.TryGetProperty("table", out var tableElement) && tableElement.ValueKind == JsonValueKind.String ? tableElement.GetString() : null,
            Columns = columns,
            Rows = rows,
            Schemas = schemas,
            Count = ReadInt(root, "count") ?? rows.Count + schemas.Count,
            TotalTableRows = ReadInt(root, "totalTableRows"),
            Limit = ReadInt(root, "limit")
        };
    }

    /// <summary>An integer cell; JSON null, a missing column and non-numeric text all read as null.</summary>
    public static int? ReadInt(IReadOnlyDictionary<string, JsonElement> row, string column) =>
        row.TryGetValue(column, out var value) ? ToInt(value) : null;

    public static string? ReadString(IReadOnlyDictionary<string, JsonElement> row, string column) =>
        row.TryGetValue(column, out var value) ? ToText(value) : null;

    public string Render()
    {
        var sb = new StringBuilder();

        if (Schemas.Count > 0)
        {
            sb.AppendLine($"{Schemas.Count} table(s):");
            foreach (var entry in Schemas)
                sb.AppendLine($"  {entry.Table}: {entry.Schema}");
            return sb.ToString().TrimEnd();
        }

        var header = Table is null ? "rows" : $"table {Table}";
        var total = TotalTableRows is { } totalRows ? $" (whole table: {totalRows} rows)" : string.Empty;
        sb.AppendLine($"{header}: {Count} row(s) returned{total}");

        if (Columns.Count > 0)
        {
            sb.AppendLine(string.Join(" | ", Columns));
            foreach (var row in Rows)
                sb.AppendLine(string.Join(" | ", Columns.Select(column => ToText(row.TryGetValue(column, out var value) ? value : default) ?? "NULL")));
        }
        else if (Rows.Count == 0)
        {
            sb.AppendLine("(no rows)");
        }

        if (MayBeTruncated)
            sb.AppendLine($"NOTE: exactly {Limit} rows came back, which is the page size, so the result may continue. Fetch the next page with LIMIT {Limit} OFFSET {Count} (then OFFSET {Count * 2}, ...) or narrow the WHERE clause.");

        return sb.ToString().TrimEnd();
    }

    private static int? ReadInt(JsonElement obj, string property) =>
        obj.TryGetProperty(property, out var value) ? ToInt(value) : null;

    private static int? ToInt(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number when value.TryGetInt32(out var number) => number,
        JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
        _ => null
    };

    private static string? ToText(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null or JsonValueKind.Undefined => null,
        _ => value.GetRawText()
    };
}
