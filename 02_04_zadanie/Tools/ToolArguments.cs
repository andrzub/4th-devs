using System.Text.Json;

namespace _02_04_zadanie.Tools;

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

    public bool? GetBool(string name) => TryGet(name, out var el)
        ? el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            // Some models answer a boolean parameter with the string "true".
            JsonValueKind.String when bool.TryParse(el.GetString(), out var parsed) => parsed,
            _ => null
        }
        : null;

    /// <summary>Accepts either a JSON array of strings or a single string.</summary>
    public IReadOnlyList<string>? GetStringArray(string name)
    {
        if (!TryGet(name, out var el))
            return null;
        if (el.ValueKind == JsonValueKind.String)
            return [el.GetString()!];
        if (el.ValueKind != JsonValueKind.Array)
            return null;
        return el.EnumerateArray()
            .Where(x => x.ValueKind is JsonValueKind.String or JsonValueKind.Number)
            .Select(x => x.ToString())
            .ToList();
    }

    private bool TryGet(string name, out JsonElement element)
    {
        if (_root.ValueKind == JsonValueKind.Object && _root.TryGetProperty(name, out element) && element.ValueKind != JsonValueKind.Null)
            return true;
        element = default;
        return false;
    }
}
