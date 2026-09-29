using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _05_02_zadanie.Hub;

/// <summary>JSON with every long string cut down to a prefix, for the log and the console: one recording is hundreds of kilobytes of Base64.</summary>
public static class JsonPreview
{
    private const int KeptPrefix = 60;

    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string ShortenLongStrings(string json, int maxLength = 200)
    {
        try
        {
            return JsonNode.Parse(json) is { } node ? Shorten(node, maxLength).ToJsonString(Readable) : json;
        }
        catch (JsonException)
        {
            return ShortenText(json, maxLength);
        }
    }

    public static JsonNode Shorten(JsonNode node, int maxLength = 200) => node switch
    {
        JsonObject obj => new JsonObject(obj.Select(property => KeyValuePair.Create(property.Key, property.Value is null ? null : Shorten(property.Value, maxLength)))),
        JsonArray array => new JsonArray(array.Select(item => item is null ? null : Shorten(item, maxLength)).ToArray()),
        JsonValue value when value.TryGetValue<string>(out var text) => JsonValue.Create(ShortenText(text, maxLength)),
        _ => node.DeepClone()
    };

    private static string ShortenText(string text, int maxLength) =>
        text.Length <= maxLength ? text : $"{text[..KeptPrefix]}... ({text.Length} chars)";
}
