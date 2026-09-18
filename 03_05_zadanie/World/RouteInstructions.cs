namespace _03_05_zadanie.World;

/// <summary>
/// The wire format of an answer: the first token names the departure mode, the rest are moves and
/// the one transition command. Parsing is tolerant about spacing and case, never about vocabulary.
/// </summary>
public static class RouteInstructions
{
    public const string Dismount = "dismount";

    private static readonly Dictionary<string, (int Row, int Col)> Offsets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["up"] = (-1, 0),
        ["down"] = (1, 0),
        ["left"] = (0, -1),
        ["right"] = (0, 1)
    };

    public static IReadOnlyCollection<string> Directions => Offsets.Keys;

    public static bool TryOffset(string token, out (int Row, int Col) offset) =>
        Offsets.TryGetValue(token.Trim(), out offset);

    public static Position Apply(Position position, (int Row, int Col) offset) =>
        new(position.Row + offset.Row, position.Col + offset.Col);

    public static bool IsDismount(string token) => string.Equals(token.Trim(), Dismount, StringComparison.OrdinalIgnoreCase);

    /// <summary>Accepts both a ready list and one line of space-separated tokens.</summary>
    public static List<string> Split(IEnumerable<string> tokens) =>
        [.. tokens.SelectMany(token => token.Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
                  .Select(token => token.Trim().ToLowerInvariant())];

    public static string Render(IEnumerable<string> instructions) => string.Join(" ", instructions);
}
