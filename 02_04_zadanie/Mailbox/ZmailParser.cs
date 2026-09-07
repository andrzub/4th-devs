using System.Text.Json;

namespace _02_04_zadanie.Mailbox;

/// <summary>
/// Turns zmail JSON into the local model. Tolerant on purpose: a missing or renamed field
/// degrades one value instead of failing a whole run, and unknown shapes are reported as errors
/// so the raw body can still be shown verbatim.
/// </summary>
public static class ZmailParser
{
    public static bool IsError(string json, out string error)
    {
        error = "";
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False)
            {
                error = root.TryGetProperty("error", out var e) ? e.ToString() : json;
                return true;
            }
            return false;
        }
        catch (JsonException)
        {
            error = "Response was not valid JSON: " + Truncate(json, 400);
            return true;
        }
    }

    public static (IReadOnlyList<MailHeader> Headers, MailPage? Page) ParseHeaders(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var headers = new List<MailHeader>();

        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            headers.AddRange(items.EnumerateArray().Select(ReadHeader));

        MailPage? page = null;
        if (root.TryGetProperty("pagination", out var p) && p.ValueKind == JsonValueKind.Object)
        {
            page = new MailPage(
                ReadInt(p, "page", 1),
                ReadInt(p, "perPage", headers.Count),
                ReadInt(p, "total", headers.Count),
                ReadInt(p, "totalPages", 1));
        }

        return (headers, page);
    }

    public static (IReadOnlyList<MailMessage> Messages, IReadOnlyList<string> NotFound) ParseMessages(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var messages = new List<MailMessage>();
        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
                messages.Add(new MailMessage(ReadHeader(item), ReadString(item, "message")));
        }

        var notFound = new List<string>();
        if (root.TryGetProperty("notFound", out var nf) && nf.ValueKind == JsonValueKind.Array)
            notFound.AddRange(nf.EnumerateArray().Select(x => x.ToString()));

        return (messages, notFound);
    }

    /// <summary>
    /// getMessages reports a running request counter for the API key. Surfacing it is worth the
    /// few characters: the mailbox has a request budget that only the 'reset' action clears.
    /// </summary>
    public static int? ReadRequestCounter(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("request", out var direct) && direct.TryGetInt32(out var value))
            return value;

        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
                if (item.TryGetProperty("request", out var nested) && nested.TryGetInt32(out var nestedValue))
                    return nestedValue;
        }

        return null;
    }

    private static MailHeader ReadHeader(JsonElement item) => new(
        ReadString(item, "rowID"),
        ReadString(item, "messageID"),
        ReadString(item, "threadID"),
        ReadString(item, "subject"),
        ReadString(item, "from"),
        ReadString(item, "to"),
        ReadString(item, "date"));

    private static string ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
            ? value.ToString()
            : "";

    private static int ReadInt(JsonElement element, string name, int fallback) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)
            ? parsed
            : fallback;

    public static string Truncate(string text, int maxChars) =>
        text.Length <= maxChars ? text : text[..maxChars] + $"...(truncated, {text.Length} chars total)";
}
