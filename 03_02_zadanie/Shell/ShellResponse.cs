using System.Text.Json;

namespace _03_02_zadanie.Shell;

/// <summary>
/// One answer from the machine's shell API, which always replies with a numeric code, a message
/// and an optional data payload. The payload arrives as a list of lines for a listing or a file,
/// and as a single string for everything else, so both shapes are flattened to lines here.
/// </summary>
public sealed record ShellResponse(int HttpStatus, int? Code, string Message, IReadOnlyList<string> Lines, string RawBody)
{
    public bool IsHttpSuccess => HttpStatus is >= 200 and < 300;

    /// <summary>The payload as it would look on a terminal.</summary>
    public string Text => string.Join(Environment.NewLine, Lines);

    public static ShellResponse Parse(int httpStatus, string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            var code = root.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var parsedCode)
                ? parsedCode
                : (int?)null;

            var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                ? messageElement.GetString() ?? string.Empty
                : string.Empty;

            return new ShellResponse(httpStatus, code, message, ReadLines(root), body);
        }
        catch (JsonException)
        {
            // A gateway error or an HTML page is still information the run needs to see.
            return new ShellResponse(httpStatus, null, "Response was not JSON.", [body], body);
        }
    }

    private static IReadOnlyList<string> ReadLines(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data))
            return [];

        return data.ValueKind switch
        {
            JsonValueKind.Array => [.. data.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String ? item.GetString() ?? string.Empty : item.ToString())],
            JsonValueKind.String => [.. (data.GetString() ?? string.Empty).ReplaceLineEndings("\n").Split('\n')],
            JsonValueKind.Null or JsonValueKind.Undefined => [],
            _ => [data.ToString()]
        };
    }

    /// <summary>
    /// What the model reads. The machine's own wording travels back unedited: its codes and
    /// messages name the exact problem, and a paraphrase could only lose that.
    /// </summary>
    public string Render(int maxCharacters)
    {
        var header = Code is { } code ? $"[{code}] {Message}" : Message;
        if (Lines.Count == 0)
            return header;

        var text = Text;
        if (text.Length > maxCharacters)
            text = text[..maxCharacters] + $"{Environment.NewLine}... output truncated at {maxCharacters} characters ({Lines.Count} lines total).";

        return $"{header}{Environment.NewLine}{text}";
    }
}
