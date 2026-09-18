using System.Text;

namespace _03_05_zadanie.World;

public readonly record struct Position(int Row, int Col)
{
    public override string ToString() => $"({Row},{Col})";
}

/// <summary>
/// A rectangular grid of terrain markers, addressed the way the hub addresses it: rows and columns
/// numbered from 1, row 1 at the north edge.
/// </summary>
public sealed class TerrainMap
{
    private readonly string[] _rows;

    private TerrainMap(string[] rows, Position start, Position goal)
    {
        _rows = rows;
        Start = start;
        Goal = goal;
    }

    public int Rows => _rows.Length;

    public int Cols => _rows[0].Length;

    public Position Start { get; }

    public Position Goal { get; }

    public char At(Position position) => _rows[position.Row - 1][position.Col - 1];

    public bool Contains(Position position) =>
        position.Row >= 1 && position.Row <= Rows && position.Col >= 1 && position.Col <= Cols;

    public IReadOnlyList<string> RawRows => _rows;

    public static bool TryCreate(IReadOnlyList<string> rows, char startMarker, char goalMarker, out TerrainMap? map, out string? error)
    {
        map = null;
        error = null;

        var cleaned = rows.Select(row => row.Trim()).Where(row => row.Length > 0).ToArray();
        if (cleaned.Length == 0)
        {
            error = "The map is empty.";
            return false;
        }

        if (cleaned.Any(row => row.Length != cleaned[0].Length))
        {
            error = $"The map is not rectangular: row lengths are {string.Join(", ", cleaned.Select(row => row.Length))}.";
            return false;
        }

        if (!TryFindSingle(cleaned, startMarker, out var start, out error) || !TryFindSingle(cleaned, goalMarker, out var goal, out error))
            return false;

        map = new TerrainMap(cleaned, start, goal);
        return true;
    }

    private static bool TryFindSingle(string[] rows, char marker, out Position position, out string? error)
    {
        var hits = new List<Position>();
        for (var row = 0; row < rows.Length; row++)
            for (var col = 0; col < rows[row].Length; col++)
                if (rows[row][col] == marker)
                    hits.Add(new Position(row + 1, col + 1));

        position = hits.Count == 1 ? hits[0] : default;
        error = hits.Count switch
        {
            0 => $"The map has no '{marker}' tile.",
            1 => null,
            _ => $"The map has {hits.Count} '{marker}' tiles and exactly one is expected."
        };
        return error is null;
    }

    /// <summary>Renders the grid with row and column numbers, so coordinates never have to be counted by eye.</summary>
    public string Render()
    {
        var sb = new StringBuilder();
        sb.Append("     ");
        for (var col = 1; col <= Cols; col++)
            sb.Append(col % 10);
        sb.AppendLine();

        for (var row = 1; row <= Rows; row++)
            sb.AppendLine($"{row,3}  {_rows[row - 1]}");

        sb.AppendLine($"start {Start}  goal {Goal}");
        return sb.ToString().TrimEnd();
    }
}
