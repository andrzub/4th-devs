using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace _05_02_zadanie.Hub;

/// <summary>A recording found in a hub reply: where it sat in the JSON, its bytes and the container its bytes say it is.</summary>
public sealed record ReplyAudio(string JsonPath, byte[] Bytes, string Extension);

public sealed partial record HubReply(int Status, string Body)
{
    private const int MinAudioBase64Length = 64;

    public bool IsSuccess => Status is >= 200 and < 300;

    /// <summary>The hub's own result code when the body is JSON with a numeric "code"; null otherwise.</summary>
    public int? Code => ReadProperty("code") is { ValueKind: JsonValueKind.Number } code ? code.GetInt32() : null;

    public string? Message => Text("message");

    /// <summary>A top-level string field of the reply, or null when it is absent or not a string.</summary>
    public string? Text(string name) => ReadProperty(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    /// <summary>What the hub says the turn was missing. It arrives with a rejection and is the closest thing this task has to documentation.</summary>
    public string? Hint => Text("hint");

    /// <summary>The hub's own transcription of the recording we sent, which is how we learn whether it heard us the way our listeners did.</summary>
    public string? HeardFromUs => Text("transcription");

    public string? Flag => FlagPattern().Match(Body) is { Success: true } match ? match.Value : null;

    public string Preview() => $"HTTP {Status}{(Code is { } code ? $" (code {code})" : "")}{Environment.NewLine}{JsonPreview.ShortenLongStrings(Body)}";

    /// <summary>
    /// Every string in the reply that decodes to a recognised audio container, wherever the hub put it:
    /// the reply's shape is only known after the first call, so no field name is assumed.
    /// </summary>
    public IReadOnlyList<ReplyAudio> FindAudio()
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(Body);
        }
        catch (JsonException)
        {
            return [];
        }

        var found = new List<ReplyAudio>();
        CollectAudio(root, "$", found);
        return found;
    }

    private static void CollectAudio(JsonNode? node, string path, List<ReplyAudio> found)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, value) in obj)
                    CollectAudio(value, $"{path}.{key}", found);
                break;
            case JsonArray array:
                for (var index = 0; index < array.Count; index++)
                    CollectAudio(array[index], $"{path}[{index}]", found);
                break;
            case JsonValue value when value.TryGetValue<string>(out var text) && TryDecodeAudio(text) is { } decoded:
                found.Add(new ReplyAudio(path, decoded.Bytes, decoded.Extension));
                break;
        }
    }

    private static (byte[] Bytes, string Extension)? TryDecodeAudio(string text)
    {
        const string dataUrlMarker = ";base64,";
        var marker = text.StartsWith("data:", StringComparison.OrdinalIgnoreCase) ? text.IndexOf(dataUrlMarker, StringComparison.OrdinalIgnoreCase) : -1;
        var payload = marker > 0 ? text[(marker + dataUrlMarker.Length)..] : text;
        if (payload.Length < MinAudioBase64Length)
            return null;

        try
        {
            var bytes = Convert.FromBase64String(payload);
            return AudioSniffer.Extension(bytes) is { } extension ? (bytes, extension) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

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

    [GeneratedRegex(@"\{FLG:[^}]+\}")]
    private static partial Regex FlagPattern();
}
