using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace _05_01_zadanie.Routing;

/// <summary>
/// Shrinks a text document without changing what it says: JSON is minified, XML loses the indentation
/// between tags. Pretty-printing is roughly a third of these files, and the model pays for every space.
/// </summary>
public static partial class DocumentCompactor
{
    private static readonly JsonSerializerOptions Minified = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Compact(byte[] content, string type)
    {
        var text = Encoding.UTF8.GetString(content).TrimStart('﻿').Trim();
        return type switch
        {
            "application/json" => TryMinifyJson(text),
            "application/xml" => WhitespaceBetweenTags().Replace(text, "><"),
            _ => text
        };
    }

    private static string TryMinifyJson(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return JsonSerializer.Serialize(document.RootElement, Minified);
        }
        catch (JsonException)
        {
            return text;
        }
    }

    [GeneratedRegex(@">\s+<")]
    private static partial Regex WhitespaceBetweenTags();
}
