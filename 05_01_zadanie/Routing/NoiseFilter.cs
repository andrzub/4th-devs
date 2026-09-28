using System.Text.RegularExpressions;

namespace _05_01_zadanie.Routing;

/// <summary>
/// Recognises radio static written out as text ("kshhh ...odbierasz mnie... bzzzt ...") and reduces it
/// to its distinct speech fragments. Static is not thrown away: repeated fragments collapse to one line,
/// so what survives costs a few tokens instead of the whole transcription.
/// </summary>
public static partial class NoiseFilter
{
    private const int MinEllipsisSegments = 5;
    private const int MinStaticSounds = 3;
    private const int MinFragmentLength = 3;

    public static bool IsNoise(string text) =>
        EllipsisSegment().Count(text) >= MinEllipsisSegments && StaticSound().Count(text) >= MinStaticSounds;

    /// <summary>Distinct fragments across the given noise texts; a fragment that is a cut-off prefix of a longer one is dropped.</summary>
    public static IReadOnlyList<string> DistinctFragments(IEnumerable<string> noiseTexts)
    {
        var fragments = noiseTexts
            .SelectMany(text => StaticSound().Replace(text, "|").Split(["...", "|", "…"], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(fragment => fragment.Trim(' ', '.', ',', '*'))
            .Where(fragment => fragment.Length >= MinFragmentLength)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return fragments
            .Where(fragment => !fragments.Any(other => other.Length > fragment.Length && other.StartsWith(fragment, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }

    [GeneratedRegex(@"\.\.\.[^.]+\.\.\.")]
    private static partial Regex EllipsisSegment();

    [GeneratedRegex(@"\*?\b(?:k?s+z*h+|k?h{2,}|bz+t*|sz{2,}|trzask(?:\s+trzask)*|pisk|szum)\b\*?", RegexOptions.IgnoreCase)]
    private static partial Regex StaticSound();
}
