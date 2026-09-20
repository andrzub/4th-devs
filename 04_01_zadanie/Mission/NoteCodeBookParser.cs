using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace _04_01_zadanie.Mission;

/// <summary>
/// Reads the classification table out of the operators' coding note. The note has a fixed shape —
/// four upper-case letters and a dash open each family, two-digit codes open each subtype — so its
/// structure can be recovered here without deciding what any code means: that is still read straight
/// from the note's own words. The recovered table is handed to <see cref="CodeBook.TryParse"/>,
/// which is the single place that says whether a table is well-formed.
///
/// The agent registers the table itself with register_codebook; this is for the by-hand path, where
/// there is no model to do it, and as a safety net.
/// </summary>
public static partial class NoteCodeBookParser
{
    [GeneratedRegex(@"[A-Z]{4}\s*-")]
    private static partial Regex FamilyHeaderRegex();

    [GeneratedRegex(@"^(?<prefix>[A-Z]{4})\s*-\s*(?<meaning>.+?)(?=\s+\d{2}(?:\s|$))", RegexOptions.Singleline)]
    private static partial Regex FamilyRegex();

    // A subtype's meaning runs until the next code, the next capitalised word (a following family
    // header or the note's closing sentence) or the end of the block. Every meaning in the note is
    // a lower-case phrase, so a capital reliably marks where the meaning stops.
    [GeneratedRegex(@"(?<code>\d{2})\s+(?<meaning>.+?)(?=\s+\d{2}(?:\s|$)|\s+\p{Lu}|$)", RegexOptions.Singleline)]
    private static partial Regex SubtypeRegex();

    /// <summary>Turns the note text into a codebook, or explains why it could not.</summary>
    public static bool TryParse(string noteText, out CodeBook? codeBook, out string error)
    {
        codeBook = null;

        if (!TryBuildJson(noteText, out var json, out error))
            return false;

        return CodeBook.TryParse(JsonDocument.Parse(json).RootElement, out codeBook, out error);
    }

    private static bool TryBuildJson(string noteText, out string json, out string error)
    {
        json = string.Empty;
        error = string.Empty;

        var text = (noteText ?? string.Empty).ReplaceLineEndings(" ");
        var headers = FamilyHeaderRegex().Matches(text);

        if (headers.Count == 0)
        {
            error = "the note has no family headers (four upper-case letters followed by a dash).";
            return false;
        }

        var builder = new StringBuilder("{\"families\":[");

        for (var i = 0; i < headers.Count; i++)
        {
            var start = headers[i].Index;
            var end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
            var block = text[start..end];

            var family = FamilyRegex().Match(block);
            if (!family.Success)
            {
                error = $"could not read the family at '{Excerpt(block)}'.";
                return false;
            }

            var subtypes = SubtypeRegex().Matches(block);
            if (subtypes.Count == 0)
            {
                error = $"family '{family.Groups["prefix"].Value}' has no subtypes in the note.";
                return false;
            }

            if (i > 0)
                builder.Append(',');

            builder.Append($"{{\"prefix\":{Json(family.Groups["prefix"].Value)},\"meaning\":{Json(family.Groups["meaning"].Value)},\"subtypes\":[");
            for (var s = 0; s < subtypes.Count; s++)
            {
                if (s > 0)
                    builder.Append(',');
                builder.Append($"{{\"code\":{Json(subtypes[s].Groups["code"].Value)},\"meaning\":{Json(subtypes[s].Groups["meaning"].Value)}}}");
            }
            builder.Append("]}");
        }

        builder.Append("]}");
        json = builder.ToString();
        return true;
    }

    private static string Json(string value) => JsonSerializer.Serialize(value.Trim());

    private static string Excerpt(string text) => text.Length <= 40 ? text : text[..40] + "...";
}
