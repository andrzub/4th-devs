using System.Text;
using System.Text.RegularExpressions;

namespace _05_01_zadanie.Routing;

/// <summary>
/// Decodes Morse spelled out as syllables: "Ti" is a dot, "Ta" a dash, a space separates letters and
/// "(stop)" separates words. Unknown sequences stay visible as <c>[?-..-]</c> instead of being guessed.
/// </summary>
public static partial class MorseDecoder
{
    private const int MinSymbols = 5;
    private const double MinSymbolShare = 0.6;
    private const string WordBreak = "(stop)";

    private static readonly Dictionary<string, char> Alphabet = new()
    {
        [".-"] = 'A', ["-..."] = 'B', ["-.-."] = 'C', ["-.."] = 'D', ["."] = 'E', ["..-."] = 'F', ["--."] = 'G', ["...."] = 'H',
        [".."] = 'I', [".---"] = 'J', ["-.-"] = 'K', [".-.."] = 'L', ["--"] = 'M', ["-."] = 'N', ["---"] = 'O', [".--."] = 'P',
        ["--.-"] = 'Q', [".-."] = 'R', ["..."] = 'S', ["-"] = 'T', ["..-"] = 'U', ["...-"] = 'V', [".--"] = 'W', ["-..-"] = 'X',
        ["-.--"] = 'Y', ["--.."] = 'Z',
        [".-.-"] = 'Ą', ["-.-.."] = 'Ć', ["..-.."] = 'Ę', [".-..-"] = 'Ł', ["--.--"] = 'Ń', ["---."] = 'Ó', ["...-..."] = 'Ś',
        ["--..-."] = 'Ź', ["--..-"] = 'Ż',
        [".----"] = '1', ["..---"] = '2', ["...--"] = '3', ["....-"] = '4', ["....."] = '5', ["-...."] = '6', ["--..."] = '7',
        ["---.."] = '8', ["----."] = '9', ["-----"] = '0',
        ["-..-."] = '/', [".-.-.-"] = '.', ["--..--"] = ',', ["..--.."] = '?', ["-....-"] = '-'
    };

    public static bool TryDecode(string text, out string decoded)
    {
        decoded = string.Empty;
        var tokens = SoundEffect().Replace(text, " ").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var symbols = tokens.Count(token => Symbol().IsMatch(token));
        var meaningful = tokens.Count(token => token != WordBreak);
        if (symbols < MinSymbols || symbols < meaningful * MinSymbolShare)
            return false;

        var output = new StringBuilder();
        foreach (var token in tokens)
        {
            if (token == WordBreak)
                output.Append(' ');
            else if (Symbol().IsMatch(token))
            {
                var code = token.Replace("Ti", ".").Replace("Ta", "-");
                output.Append(Alphabet.TryGetValue(code, out var letter) ? letter.ToString() : $"[?{code}]");
            }
        }

        decoded = output.ToString().Trim();
        return true;
    }

    [GeneratedRegex(@"^(?:Ti|Ta)+$")]
    private static partial Regex Symbol();

    [GeneratedRegex(@"\*[^*]*\*")]
    private static partial Regex SoundEffect();
}
