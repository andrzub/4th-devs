using System.Globalization;
using System.Text.Json;

namespace _02_03_zadanie.Tools;

/// <summary>
/// Tolerant reader of the JSON arguments the model passes to a tool: missing, null or
/// mistyped values read as null, so each tool can fall back to its defaults.
/// </summary>
public sealed class ToolArguments
{
    private readonly JsonElement _root;

    private ToolArguments(JsonElement root) => _root = root;

    public static ToolArguments Parse(string json)
    {
        using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        return new ToolArguments(doc.RootElement.Clone());
    }

    public string? GetString(string name) =>
        TryGet(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    public int? GetInt(string name) =>
        TryGet(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var value) ? value : null;

    /// <summary>Accepts either a JSON array of strings or a single string.</summary>
    public IReadOnlyList<string>? GetStringArray(string name)
    {
        if (!TryGet(name, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.String)
            return [el.GetString()!];
        if (el.ValueKind != JsonValueKind.Array)
            return null;
        return el.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList();
    }

    /// <summary>Parses "HH:MM" or "H:MM"; seconds are tolerated and ignored.</summary>
    public TimeOnly? GetTime(string name)
    {
        var text = GetString(name);
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return TimeOnly.TryParseExact(text.Trim(), ["HH:mm", "H:mm", "HH:mm:ss", "H:mm:ss"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : null;
    }

    private bool TryGet(string name, out JsonElement element)
    {
        if (_root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(name, out element) && element.ValueKind != JsonValueKind.Null)
            return true;
        element = default;
        return false;
    }
}
