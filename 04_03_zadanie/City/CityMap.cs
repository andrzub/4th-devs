using System.Text;
using System.Text.Json.Nodes;

namespace _04_03_zadanie.City;

public sealed record TileKind(string Name, string Symbol, string Label);

public sealed record Tile(Coordinate Position, TileKind Kind)
{
    public bool IsRoad => Kind.Name == "road";

    public string Symbol => Kind.Symbol;
}

/// <summary>
/// The city exactly as getMap serves it: a square grid of tile kinds plus the legend mapping a kind to
/// its two-character symbol. Nothing about the terrain is written into code; the only kind the code
/// names is "road", because help says transporters drive on nothing else.
/// </summary>
public sealed class CityMap
{
    private readonly Tile[,] _tiles;

    private CityMap(int size, Tile[,] tiles, IReadOnlyDictionary<string, TileKind> legend)
    {
        Size = size;
        _tiles = tiles;
        Legend = legend;
    }

    public int Size { get; }

    public IReadOnlyDictionary<string, TileKind> Legend { get; }

    public Tile this[Coordinate position] => _tiles[position.Column - 1, position.Row - 1];

    public IEnumerable<Tile> Tiles
    {
        get
        {
            for (var row = 1; row <= Size; row++)
                for (var column = 1; column <= Size; column++)
                    yield return _tiles[column - 1, row - 1];
        }
    }

    public bool IsRoad(Coordinate position) => position.IsOnBoard && this[position].IsRoad;

    public IReadOnlyList<Coordinate> FieldsWithSymbol(string symbol) =>
        Tiles.Where(tile => tile.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase)).Select(tile => tile.Position).ToList();

    public TileKind? KindOfSymbol(string symbol) =>
        Legend.Values.FirstOrDefault(kind => kind.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));

    public static CityMap Load(string path) => Parse(File.ReadAllText(path));

    /// <summary>Accepts the whole getMap reply or just its "map" object.</summary>
    public static CityMap Parse(string json)
    {
        var root = JsonNode.Parse(json) as JsonObject ?? throw new FormatException("Map JSON is not an object.");
        var map = root["map"] as JsonObject ?? root;
        var grid = map["grid"] as JsonArray ?? throw new FormatException("Map JSON has no grid.");
        var tileKinds = map["tiles"] as JsonObject ?? throw new FormatException("Map JSON has no tile legend.");
        var size = map["size"]?.GetValue<int>() ?? grid.Count;

        if (size != Coordinate.BoardSize)
            throw new FormatException($"Map size is {size}, coordinates A1..K11 assume {Coordinate.BoardSize}.");
        if (grid.Count != size)
            throw new FormatException($"Grid has {grid.Count} rows, size says {size}.");

        var legend = new Dictionary<string, TileKind>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, node) in tileKinds)
            legend[name] = new TileKind(name, node?["symbol"]?.GetValue<string>() ?? "??", node?["label"]?.GetValue<string>() ?? name);

        var tiles = new Tile[size, size];
        for (var row = 0; row < size; row++)
        {
            var cells = grid[row] as JsonArray ?? throw new FormatException($"Row {row + 1} is not an array.");
            if (cells.Count != size)
                throw new FormatException($"Row {row + 1} has {cells.Count} fields, expected {size}.");

            for (var column = 0; column < size; column++)
            {
                var kindName = cells[column]?.GetValue<string>() ?? throw new FormatException($"Row {row + 1}, column {column + 1} is empty.");
                if (!legend.TryGetValue(kindName, out var kind))
                    throw new FormatException($"Row {row + 1}, column {column + 1}: unknown tile kind '{kindName}'.");

                tiles[column, row] = new Tile(new Coordinate(column + 1, row + 1), kind);
            }
        }

        return new CityMap(size, tiles, legend);
    }

    /// <summary>
    /// The board as the preview draws it, two characters per field. Empty ground has a blank symbol,
    /// shown as dots so the columns stay aligned; an overlay replaces a field's symbol, e.g. with a unit.
    /// </summary>
    public string Render(IReadOnlyDictionary<Coordinate, string>? overlay = null)
    {
        var text = new StringBuilder();
        text.Append("    ");
        for (var column = 1; column <= Size; column++)
            text.Append($"{(char)('A' + column - 1),2} ");
        text.AppendLine();

        for (var row = 1; row <= Size; row++)
        {
            text.Append($"{row,2}  ");
            for (var column = 1; column <= Size; column++)
            {
                var position = new Coordinate(column, row);
                var symbol = overlay is not null && overlay.TryGetValue(position, out var marker) ? marker : this[position].Symbol;
                if (string.IsNullOrWhiteSpace(symbol))
                    symbol = "..";
                text.Append($"{symbol,2} ");
            }
            text.AppendLine();
        }

        return text.ToString().TrimEnd();
    }
}
