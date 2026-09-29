using System.Text;

namespace _05_02_zadanie.Voice;

/// <summary>
/// Decides whether a transcript still carries the terms a line exists to deliver. The comparison ignores case,
/// Polish diacritics, spaces and punctuation, so "RD 224" and "rd-224" both count as RD224, while a code heard
/// as words ("er de dwieście…") does not: that is exactly the failure the check is there to catch.
/// </summary>
public static class TranscriptCheck
{
    public static IReadOnlyList<string> MissingTerms(string transcript, IEnumerable<string> requiredTerms)
    {
        var heard = Normalize(transcript);
        return requiredTerms.Where(term => !heard.Contains(Normalize(term), StringComparison.Ordinal)).ToList();
    }

    public static string Normalize(string text)
    {
        // "ł" has no decomposition, so it would otherwise be dropped instead of folded to "l".
        var decomposed = text.ToLowerInvariant().Replace('ł', 'l').Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
                folded.Append(c);
        }

        return folded.ToString();
    }
}
