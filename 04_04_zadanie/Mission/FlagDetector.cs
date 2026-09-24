using System.Text.RegularExpressions;

namespace _04_04_zadanie.Mission;

/// <summary>The flag is recognised by code in the hub's reply, never transcribed from anywhere else.</summary>
public static partial class FlagDetector
{
    [GeneratedRegex(@"\{FLG:[^}]+\}")]
    private static partial Regex FlagPattern();

    public static string? Find(string text) => FlagPattern().Match(text) is { Success: true } match ? match.Value : null;
}
