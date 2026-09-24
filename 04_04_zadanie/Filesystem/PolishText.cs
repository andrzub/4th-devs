namespace _04_04_zadanie.Filesystem;

/// <summary>
/// The one transformation the task asks for on names: Polish letters replaced by their base
/// letters. Nothing else is touched, so a name that comes back unchanged was already plain ASCII.
/// </summary>
public static class PolishText
{
    private static readonly Dictionary<char, char> Folds = new()
    {
        ['ą'] = 'a', ['ć'] = 'c', ['ę'] = 'e', ['ł'] = 'l', ['ń'] = 'n', ['ó'] = 'o', ['ś'] = 's', ['ź'] = 'z', ['ż'] = 'z',
        ['Ą'] = 'A', ['Ć'] = 'C', ['Ę'] = 'E', ['Ł'] = 'L', ['Ń'] = 'N', ['Ó'] = 'O', ['Ś'] = 'S', ['Ź'] = 'Z', ['Ż'] = 'Z'
    };

    public static string Fold(string text) =>
        new(text.Select(character => Folds.TryGetValue(character, out var folded) ? folded : character).ToArray());

    public static bool IsAscii(string text) => text.All(character => character < 128);

    public static string NonAsciiPreview(string text) =>
        string.Join(" ", text.Where(character => character >= 128).Distinct().Take(8));
}
