using System.Text;
using System.Text.Json;

namespace _04_01_zadanie.Mission;

public sealed record CodeSubtype(string Digits, string Meaning);

public sealed record CodeFamily(string Prefix, string Meaning, IReadOnlyList<CodeSubtype> Subtypes);

/// <summary>
/// The classification codes the console keeps at the front of an incident title, as the agent read
/// them out of the operators' own note. The shape is checked here, never the truth of it: code has
/// no business knowing that animals are "04", but it can insist that the agent went and read the
/// whole table instead of guessing one entry — and afterwards it can refuse any title whose code is
/// not in the table that was read.
/// </summary>
public sealed class CodeBook
{
    private CodeBook(IReadOnlyList<CodeFamily> families) => Families = families;

    public IReadOnlyList<CodeFamily> Families { get; }

    public static bool TryParse(JsonElement root, out CodeBook? codeBook, out string error)
    {
        codeBook = null;
        error = string.Empty;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("families", out var familiesElement) || familiesElement.ValueKind != JsonValueKind.Array)
        {
            error = "expected an object with a 'families' array, each entry being {prefix, meaning, subtypes:[{code, meaning}]}.";
            return false;
        }

        var families = new List<CodeFamily>();

        foreach (var familyElement in familiesElement.EnumerateArray())
        {
            var prefix = ReadString(familyElement, "prefix");
            var meaning = ReadString(familyElement, "meaning");

            if (prefix is not { Length: 4 } || !prefix.All(char.IsAsciiLetterUpper))
            {
                error = $"'{prefix ?? "(missing)"}' is not a family prefix: the console uses exactly four upper-case letters.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(meaning))
            {
                error = $"family '{prefix}' was registered without a meaning — record what the note says it stands for.";
                return false;
            }

            if (families.Any(existing => existing.Prefix == prefix))
            {
                error = $"family '{prefix}' was registered twice.";
                return false;
            }

            if (!familyElement.TryGetProperty("subtypes", out var subtypesElement) || subtypesElement.ValueKind != JsonValueKind.Array)
            {
                error = $"family '{prefix}' has no 'subtypes' array.";
                return false;
            }

            var subtypes = new List<CodeSubtype>();

            foreach (var subtypeElement in subtypesElement.EnumerateArray())
            {
                var digits = ReadString(subtypeElement, "code");
                var subtypeMeaning = ReadString(subtypeElement, "meaning");

                if (digits is not { Length: 2 } || !digits.All(char.IsAsciiDigit))
                {
                    error = $"'{digits ?? "(missing)"}' is not a subtype of '{prefix}': subtypes are exactly two digits.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(subtypeMeaning))
                {
                    error = $"subtype '{prefix}{digits}' was registered without a meaning.";
                    return false;
                }

                if (subtypes.Any(existing => existing.Digits == digits))
                {
                    error = $"subtype '{prefix}{digits}' was registered twice.";
                    return false;
                }

                subtypes.Add(new CodeSubtype(digits, subtypeMeaning.Trim()));
            }

            if (subtypes.Count < 2)
            {
                error = $"family '{prefix}' was registered with {subtypes.Count} subtype(s). Register the whole table the note lists, not only the entry you intend to use.";
                return false;
            }

            families.Add(new CodeFamily(prefix, meaning.Trim(), subtypes));
        }

        if (families.Count < 2)
        {
            error = $"only {families.Count} family/families registered. The console classifies incidents with several of them — register what the note actually lists.";
            return false;
        }

        codeBook = new CodeBook(families);
        return true;
    }

    public bool Contains(string? code) => Describe(code) is not null;

    public string? Describe(string? code)
    {
        if (code is not { Length: 6 })
            return null;

        var family = Families.FirstOrDefault(candidate => candidate.Prefix == code[..4]);
        var subtype = family?.Subtypes.FirstOrDefault(candidate => candidate.Digits == code[4..]);

        return subtype is null ? null : $"{code} = {family!.Meaning} / {subtype.Meaning}";
    }

    /// <summary>Looks up a code by what the agent recorded it as meaning, e.g. the family "MOVE" and "zwierz".</summary>
    public string? FindCode(string familyPrefix, params string[] meaningFragments)
    {
        var family = Families.FirstOrDefault(candidate => candidate.Prefix == familyPrefix);
        var subtype = family?.Subtypes.FirstOrDefault(candidate => TextMatch.ContainsAny(candidate.Meaning, meaningFragments));

        return subtype is null ? null : familyPrefix + subtype.Digits;
    }

    /// <summary>True when the code's own recorded meaning matches one of the fragments.</summary>
    public bool MeansAnyOf(string? code, params string[] meaningFragments)
    {
        if (code is not { Length: 6 })
            return false;

        var family = Families.FirstOrDefault(candidate => candidate.Prefix == code[..4]);
        var subtype = family?.Subtypes.FirstOrDefault(candidate => candidate.Digits == code[4..]);

        return subtype is not null && TextMatch.ContainsAny(subtype.Meaning, meaningFragments);
    }

    public string Render()
    {
        var builder = new StringBuilder();

        foreach (var family in Families)
        {
            builder.AppendLine($"{family.Prefix} - {family.Meaning}");
            foreach (var subtype in family.Subtypes)
                builder.AppendLine($"  {family.Prefix}{subtype.Digits} - {subtype.Meaning}");
        }

        return builder.ToString().TrimEnd();
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;
}
