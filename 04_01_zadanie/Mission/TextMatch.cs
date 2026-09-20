using System.Globalization;
using System.Text;

namespace _04_01_zadanie.Mission;

/// <summary>
/// Matching over Polish text written by whoever happened to type it. The console, the notes and the
/// model all spell the same word with and without diacritics, so comparisons are made on a stripped,
/// lower-case form rather than on the exact characters.
/// </summary>
public static class TextMatch
{
    public static string Normalise(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
                continue;

            // The stroke in "ł" is part of the letter rather than a combining mark, so it survives
            // decomposition and has to be folded by hand.
            builder.Append(character is 'ł' or 'Ł' ? 'l' : char.ToLowerInvariant(character));
        }

        return builder.ToString();
    }

    public static bool Contains(string? haystack, string needle) => Normalise(haystack).Contains(Normalise(needle), StringComparison.Ordinal);

    public static bool ContainsAny(string? haystack, params string[] needles) => needles.Any(needle => Contains(haystack, needle));
}
