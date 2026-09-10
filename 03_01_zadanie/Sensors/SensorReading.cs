using System.Globalization;
using System.Text.Json;

namespace _03_01_zadanie.Sensors;

/// <summary>
/// A single sensor file. <see cref="Id"/> is the four-digit file name without extension —
/// the identifier the hub expects back.
/// </summary>
public sealed class SensorReading
{
    public required string Id { get; init; }
    public required string SensorType { get; init; }
    public long Timestamp { get; init; }
    public required string OperatorNotes { get; init; }

    /// <summary>Channel name to reported value, for every channel present in the file.</summary>
    public required IReadOnlyDictionary<string, double> Values { get; init; }

    /// <summary>Channel names listed in <c>sensor_type</c>, split on '/'.</summary>
    public required IReadOnlyList<string> DeclaredChannels { get; init; }

    /// <summary>Declared channel names that do not match any known channel.</summary>
    public IEnumerable<string> UnknownChannels => DeclaredChannels.Where(name => SensorChannel.ByName(name) is null);

    public bool IsActive(SensorChannel channel) =>
        DeclaredChannels.Any(name => string.Equals(name, channel.Name, StringComparison.OrdinalIgnoreCase));

    public static SensorReading Parse(string id, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var sensorType = root.TryGetProperty("sensor_type", out var typeEl) ? typeEl.GetString() ?? "" : "";
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var channel in SensorChannel.All)
        {
            if (root.TryGetProperty(channel.JsonField, out var valueEl) && TryReadNumber(valueEl, out var value))
                values[channel.Name] = value;
        }

        return new SensorReading
        {
            Id = id,
            SensorType = sensorType,
            Timestamp = root.TryGetProperty("timestamp", out var ts) && ts.TryGetInt64(out var tsValue) ? tsValue : 0,
            OperatorNotes = root.TryGetProperty("operator_notes", out var notes) ? notes.GetString() ?? "" : "",
            Values = values,
            DeclaredChannels = sensorType.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        };
    }

    /// <summary>
    /// Accepts numbers written either as JSON numbers or as quoted strings, so a file that
    /// stringifies a reading is measured rather than silently skipped.
    /// </summary>
    private static bool TryReadNumber(JsonElement element, out double value)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out value);
            case JsonValueKind.String:
                return double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            default:
                value = 0;
                return false;
        }
    }
}
