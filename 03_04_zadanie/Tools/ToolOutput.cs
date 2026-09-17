using System.Globalization;
using System.Text;

namespace _03_04_zadanie.Tools;

/// <summary>
/// Every answer leaves through here. The agent aborts the whole mission when a tool stays silent
/// and the central rejects anything outside 4..500 bytes, so the budget is enforced on composition
/// instead of being hoped for. The text is folded to ASCII, and the budget counts each character
/// the way it travels inside the JSON body — a newline or a quote is two characters there, and it
/// is not knowable from here which of the two lengths the central measures.
/// </summary>
public static class ToolOutput
{
    public const int MaxBytes = 500;
    public const int MinBytes = 4;

    private const string Placeholder = "brak danych";

    public static string Compose(params string?[] lines) => Compose((IEnumerable<string?>)lines);

    public static string Compose(IEnumerable<string?> lines)
    {
        var builder = new StringBuilder();
        var used = 0;
        string? first = null;

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var candidate = ToAscii(line.Trim());
            first ??= candidate;
            var cost = WireLength(candidate) + (builder.Length == 0 ? 0 : 2);

            if (used + cost > MaxBytes)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('\n');
            }

            builder.Append(candidate);
            used += cost;
        }

        // A single line too long for the budget is still worth its beginning: silence ends the run.
        return builder.Length > 0 ? builder.ToString() : Clamp(first);
    }

    public static string Clamp(string? text)
    {
        var ascii = ToAscii(text ?? string.Empty);

        while (ascii.Length > 0 && WireLength(ascii) > MaxBytes)
        {
            ascii = ascii[..^1];
        }

        return EnsureMinimum(ascii);
    }

    /// <summary>
    /// Shortens a value the agent supplied before it is quoted back, so a long query cannot eat the
    /// budget that the actual answer needs.
    /// </summary>
    public static string Shorten(string? text, int maxLength)
    {
        var ascii = ToAscii(text ?? string.Empty).Replace('\n', ' ').Trim();
        return ascii.Length <= maxLength ? ascii : ascii[..Math.Max(1, maxLength - 3)] + "...";
    }

    public static int WireLength(string text)
    {
        var escaped = text.Count(character => character is '\n' or '\r' or '\t' or '"' or '\\');
        return text.Length + escaped;
    }

    private static string EnsureMinimum(string text) => text.Length >= MinBytes ? text : Placeholder;

    private static string ToAscii(string text)
    {
        if (text.All(char.IsAscii))
        {
            return text;
        }

        var decomposed = text.Replace('ł', 'l').Replace('Ł', 'L').Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsAscii(character) ? character : '?');
        }

        return builder.ToString();
    }
}
