using System.Globalization;
using System.Text.Json;
using _05_01_zadanie.Routing;

namespace _05_01_zadanie.Analysis;

public sealed record CityRecord(string Name, decimal Area, string AreaField, int SourceCapture);

/// <summary>
/// City records found in intercepted JSON files: any array of objects with a name and a numeric field
/// whose name mentions an area. The area in the report comes from here, never from the model's arithmetic.
/// </summary>
public sealed class CityTable
{
    private static readonly string[] NameFields = ["name", "nazwa", "miasto", "city"];

    private readonly Dictionary<string, CityRecord> _records = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<CityRecord> Records => _records.Values;

    public static CityTable From(IEnumerable<RoutedCapture> routed)
    {
        var table = new CityTable();
        foreach (var item in routed.Where(item => item.Route == Route.Document && item.LocalText is not null))
            table.AddFrom(item.LocalText!, item.Capture.Number);
        return table;
    }

    public CityRecord? Find(string name) => _records.GetValueOrDefault(name.Trim());

    /// <summary>Half away from zero, at the decimal value the file holds: 0.125 is 0.13, which binary doubles and banker's rounding both get wrong.</summary>
    public static string FormatArea(decimal area) => Math.Round(area, 2, MidpointRounding.AwayFromZero).ToString("F2", CultureInfo.InvariantCulture);

    private void AddFrom(string json, int captureNumber)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return;

            foreach (var element in document.RootElement.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object))
            {
                var properties = element.EnumerateObject().ToList();
                var name = properties.FirstOrDefault(p => NameFields.Contains(p.Name, StringComparer.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.String);
                var area = properties.FirstOrDefault(p => p.Name.Contains("area", StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind == JsonValueKind.Number);
                if (name.Value.ValueKind == JsonValueKind.Undefined || area.Value.ValueKind == JsonValueKind.Undefined)
                    continue;

                var value = decimal.Parse(area.Value.GetRawText(), NumberStyles.Float, CultureInfo.InvariantCulture);
                _records.TryAdd(name.Value.GetString()!, new CityRecord(name.Value.GetString()!, value, area.Name, captureNumber));
            }
        }
    }
}
