using System.Globalization;
using System.Text;

namespace _04_05_zadanie.Warehouse;

/// <summary>
/// City names arrive in three spellings: lowercase ASCII in the demand file, capitalised in the
/// database, and however the model types them. Comparisons fold all three to one form.
/// </summary>
public static class TextNormalizer
{
    public static string Fold(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var decomposed = text.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            sb.Append(ch switch { 'ł' => 'l', 'Ł' => 'L', _ => ch });
        }

        return sb.ToString().Normalize(NormalizationForm.FormC).Trim().ToLowerInvariant();
    }

    public static bool SameName(string? a, string? b) => Fold(a) == Fold(b) && Fold(a).Length > 0;
}
