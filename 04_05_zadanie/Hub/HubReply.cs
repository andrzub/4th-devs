using System.Text.Json;

namespace _04_05_zadanie.Hub;

public sealed record HubReply(int Status, string Body)
{
    public bool IsSuccess => Status is >= 200 and < 300;

    /// <summary>The hub's own result code when the body is JSON with a numeric "code"; null otherwise.</summary>
    public int? Code => ReadProperty("code") is { ValueKind: JsonValueKind.Number } code ? code.GetInt32() : null;

    public string? Message => ReadProperty("message") is { ValueKind: JsonValueKind.String } message ? message.GetString() : null;

    public string Describe() => $"HTTP {Status}{(Code is { } code ? $" (code {code})" : "")}{Environment.NewLine}{Body}";

    private JsonElement? ReadProperty(string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(Body);
            return doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty(name, out var value)
                ? value.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
