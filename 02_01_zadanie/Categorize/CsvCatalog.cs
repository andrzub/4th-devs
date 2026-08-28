using System.Text;

namespace _02_01_zadanie.Categorize;

public sealed record CatalogItem(string Id, string Description);

/// <summary>
/// Parser for the hub's categorize.csv. The exact shape is not documented, so parsing is defensive:
/// the delimiter is detected from the content, an optional header row is dropped, and surplus
/// columns are folded back into the description.
/// </summary>
public static class CsvCatalog
{
    public static IReadOnlyList<CatalogItem> Parse(string csv)
    {
        var lines = csv.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
            return Array.Empty<CatalogItem>();

        var delimiter = DetectDelimiter(lines[0]);
        var items = new List<CatalogItem>();

        foreach (var line in lines)
        {
            var fields = SplitLine(line, delimiter);
            if (fields.Count < 2)
                continue;

            items.Add(new CatalogItem(fields[0].Trim(), string.Join(delimiter, fields.Skip(1)).Trim()));
        }

        if (items.Count > 0 && LooksLikeHeader(items[0]))
            items.RemoveAt(0);

        return items;
    }

    private static char DetectDelimiter(string firstLine) =>
        !firstLine.Contains(',') && firstLine.Contains(';') ? ';' : ',';

    private static bool LooksLikeHeader(CatalogItem first)
    {
        var id = first.Id.ToLowerInvariant();
        var description = first.Description.ToLowerInvariant();
        return id is "id" or "identyfikator" || description is "description" or "opis" or "name" or "nazwa";
    }

    private static List<string> SplitLine(string line, char delimiter)
    {
        // Minimal quoted-field support: quotes wrap a field, "" escapes a quote inside one.
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];

            if (inQuotes)
            {
                if (ch == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (ch == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(ch);
                }
            }
            else if (ch == '"' && current.Length == 0)
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }

        fields.Add(current.ToString());
        return fields;
    }
}
