using System.Globalization;
using System.Text;

namespace _03_04_zadanie.Catalog;

/// <summary>
/// Turns free text — a catalog name or whatever the agent typed — into comparable tokens.
/// The agent writes in natural language ("potrzebuje turbiny wiatrowej na 48 V"), the catalog
/// writes in shop language ("Turbina wiatrowa 400W 48V"), so both sides go through the same mill.
/// </summary>
public static class TextNormalizer
{
    private static readonly HashSet<string> Units = new(StringComparer.Ordinal)
    {
        "v", "mv", "kv", "w", "mw", "kw", "kwh", "wh", "a", "ma", "ah", "mah", "va",
        "hz", "khz", "mhz", "ghz", "ohm", "kohm", "mohm", "f", "uf", "nf", "pf",
        "h", "mh", "uh", "mm", "cm", "m", "km", "g", "kg", "db", "dbi", "bit", "kb", "mb",
    };

    private static readonly Dictionary<string, string> UnitAliases = new(StringComparer.Ordinal)
    {
        ["volt"] = "v", ["volts"] = "v", ["wolt"] = "v", ["wolta"] = "v", ["woltow"] = "v",
        ["watt"] = "w", ["watts"] = "w", ["wat"] = "w", ["wata"] = "w", ["watow"] = "w",
        ["amper"] = "a", ["ampere"] = "a", ["amp"] = "a", ["amps"] = "a", ["amperow"] = "a",
        ["amperogodzin"] = "ah", ["amperogodziny"] = "ah",
        ["om"] = "ohm", ["oma"] = "ohm", ["omow"] = "ohm", ["ohms"] = "ohm",
        ["herc"] = "hz", ["hertz"] = "hz",
        ["metr"] = "m", ["metra"] = "m", ["metrow"] = "m", ["meter"] = "m", ["meters"] = "m",
        ["milimetr"] = "mm", ["milimetrow"] = "mm",
    };

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "potrzebuje", "potrzebny", "potrzebna", "potrzebne", "potrzeba", "szukam",
        "znajdz", "poszukuje", "chce", "chcialbym", "chcialabym", "prosze",
        "gdzie", "kupic", "kupie", "kupno", "sprzedaje", "oferuje", "oferta", "dostepny",
        "miasto", "miasta", "miast", "miastach", "ktore", "ktory", "ktora", "jakie", "jaki", "jaka",
        "ma", "maja", "mam", "jest", "sa", "byc", "moze", "moge", "musi", "mi", "nam", "nasz", "nasza",
        "do", "dla", "na", "od", "po", "przy", "pod", "nad", "za", "ze", "z", "w", "we", "i", "oraz",
        "lub", "albo", "a", "to", "te", "ten", "ta", "tego", "sztuk", "sztuka", "szt", "kilka", "jeden",
        "napiecie", "napieciu", "napiecia", "moc", "mocy", "pojemnosc", "voltage", "power",
        "need", "want", "find", "search", "looking", "look", "for", "the", "an", "some", "any",
        "where", "which", "city", "cities", "town", "towns", "buy", "purchase", "sell", "sells",
        "available", "offer", "offers", "please", "give", "list", "show", "with", "and", "or", "of",
        "is", "are", "item", "items", "part", "parts", "przedmiot", "przedmioty", "czesc", "czesci",
    };

    public static string ToAsciiLower(string text)
    {
        var lowered = text.ToLowerInvariant().Replace('ł', 'l');
        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    public static IReadOnlyList<string> Tokenize(string text, bool dropStopWords)
    {
        var raw = SplitOnPunctuation(ToAsciiLower(text));
        var merged = MergeNumbersWithUnits(raw);

        if (!dropStopWords)
        {
            return merged;
        }

        var kept = merged.Where(token => !StopWords.Contains(token)).ToList();

        // A query made of nothing but filler is better answered from the filler than from silence.
        return kept.Count > 0 ? kept : merged;
    }

    public static bool TryParseMeasurement(string token, out string unit, out double value)
    {
        unit = string.Empty;
        value = 0;

        var digits = 0;
        while (digits < token.Length && (char.IsAsciiDigit(token[digits]) || token[digits] == '.'))
        {
            digits++;
        }

        if (digits == 0 || digits == token.Length)
        {
            return false;
        }

        if (!Units.Contains(token[digits..]))
        {
            return false;
        }

        if (!double.TryParse(token[..digits], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        unit = token[digits..];
        return true;
    }

    public static bool TryResolveUnit(string token, out string unit)
    {
        if (Units.Contains(token))
        {
            unit = token;
            return true;
        }

        return UnitAliases.TryGetValue(token, out unit!);
    }

    private static List<string> SplitOnPunctuation(string ascii)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();

        foreach (var character in ascii)
        {
            var keepsDecimalPoint = character == '.' && builder.Length > 0 && char.IsAsciiDigit(builder[^1]);

            if (char.IsAsciiLetterOrDigit(character) || keepsDecimalPoint)
            {
                builder.Append(character);
                continue;
            }

            Flush(tokens, builder);
        }

        Flush(tokens, builder);
        return tokens;
    }

    private static void Flush(List<string> tokens, StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        var token = builder.ToString().Trim('.');
        if (token.Length > 0)
        {
            tokens.Add(token);
        }

        builder.Clear();
    }

    private static List<string> MergeNumbersWithUnits(List<string> tokens)
    {
        var merged = new List<string>(tokens.Count);

        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var isNumber = token.All(character => char.IsAsciiDigit(character) || character == '.');

            if (isNumber && index + 1 < tokens.Count && TryResolveUnit(tokens[index + 1], out var unit))
            {
                merged.Add(token + unit);
                index++;
                continue;
            }

            merged.Add(token);
        }

        return merged;
    }
}
