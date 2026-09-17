using System.Text.RegularExpressions;

namespace _03_04_zadanie.Catalog;

/// <summary>
/// Cuts one natural-language sentence into the individual goods it asks about. The agent writes
/// its shopping list as prose, and a fragment carrying only a rating ("48 V") is a continuation of
/// the previous item rather than an item of its own.
/// </summary>
public static partial class QuerySplitter
{
    public static IReadOnlyList<string> Split(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        var parts = Separators().Split(query)
            .Select(part => part.Trim(' ', '\t', '-', ':', '.', '"'))
            .Where(part => part.Length > 0)
            .ToList();

        var merged = new List<string>();
        foreach (var part in parts)
        {
            if (merged.Count > 0 && !CarriesItemName(part))
            {
                merged[^1] = merged[^1] + " " + part;
                continue;
            }

            merged.Add(part);
        }

        return merged;
    }

    private static bool CarriesItemName(string part)
    {
        var tokens = Synonyms.Expand(TextNormalizer.Tokenize(part, dropStopWords: true));

        return tokens.Any(token =>
            !TextNormalizer.TryParseMeasurement(token, out _, out _)
            && !TextNormalizer.TryResolveUnit(token, out _)
            && !token.All(character => char.IsAsciiDigit(character) || character == '.'));
    }

    [GeneratedRegex(@"[\r\n;,+&]|\s+(?:oraz|and|plus)\s+|\s+i\s+", RegexOptions.IgnoreCase)]
    private static partial Regex Separators();
}
