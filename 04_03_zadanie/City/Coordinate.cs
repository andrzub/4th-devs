namespace _04_03_zadanie.City;

/// <summary>
/// A board field in the hub's own notation: column letter A..K followed by row number 1..11.
/// Column and row stay 1-based so the printed form and the arithmetic agree.
/// </summary>
public readonly record struct Coordinate(int Column, int Row) : IComparable<Coordinate>
{
    public const int BoardSize = 11;

    public static bool TryParse(string? text, out Coordinate coordinate)
    {
        coordinate = default;
        var value = text?.Trim().ToUpperInvariant();
        if (string.IsNullOrEmpty(value) || value.Length is < 2 or > 3)
            return false;

        var column = value[0] - 'A' + 1;
        if (column is < 1 or > BoardSize || !int.TryParse(value[1..], out var row) || row is < 1 or > BoardSize)
            return false;

        coordinate = new Coordinate(column, row);
        return true;
    }

    public static Coordinate Parse(string text) =>
        TryParse(text, out var coordinate) ? coordinate : throw new FormatException($"'{text}' is not a board field (A1..K11).");

    public bool IsOnBoard => Column is >= 1 and <= BoardSize && Row is >= 1 and <= BoardSize;

    /// <summary>The up to four orthogonal neighbours that lie on the board.</summary>
    public IEnumerable<Coordinate> Neighbours()
    {
        Coordinate[] candidates = [new(Column, Row - 1), new(Column + 1, Row), new(Column, Row + 1), new(Column - 1, Row)];
        return candidates.Where(candidate => candidate.IsOnBoard);
    }

    public int DistanceTo(Coordinate other) => Math.Abs(Column - other.Column) + Math.Abs(Row - other.Row);

    /// <summary>Row-major order, the order the board is read in.</summary>
    public int CompareTo(Coordinate other) => Row != other.Row ? Row.CompareTo(other.Row) : Column.CompareTo(other.Column);

    public override string ToString() => $"{(char)('A' + Column - 1)}{Row}";
}
