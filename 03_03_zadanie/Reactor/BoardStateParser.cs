using System.Text;
using System.Text.Json;

namespace _03_03_zadanie.Reactor;

/// <summary>
/// Reads a board out of any JSON the reactor endpoints return. The preview backend and the hub's
/// own replies do not have to agree on where the state sits or how the fields are spelled, so the
/// parser hunts for the first object carrying a "board" and accepts both naming conventions.
/// </summary>
public static class BoardStateParser
{
    public static BoardState? TryParse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return FindBoard(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static BoardState? FindBoard(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (TryGet(element, out var boardElement, "board", "map", "grid") && boardElement.ValueKind == JsonValueKind.Array)
                return Build(element, boardElement);

            foreach (var property in element.EnumerateObject())
            {
                var nested = FindBoard(property.Value);
                if (nested is not null)
                    return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                var nested = FindBoard(item);
                if (nested is not null)
                    return nested;
            }
        }

        return null;
    }

    private static BoardState Build(JsonElement owner, JsonElement boardElement)
    {
        var rows = ReadRows(boardElement);
        var blocks = ReadBlocks(owner);

        return new BoardState
        {
            Rows = rows,
            Blocks = blocks,
            Robot = ReadRobot(owner, rows),
            ReachedGoal = ReadBool(owner, "reached_goal", "reachedGoal", "goal_reached"),
            IsCrushed = ReadBool(owner, "is_crushed", "isCrushed", "crushed"),
            Message = ReadString(owner, "crush_message", "crushMessage") ?? ReadString(owner, "message", "msg")
        };
    }

    /// <summary>Accepts a row written either as an array of cells or as one string of cells.</summary>
    private static List<string> ReadRows(JsonElement boardElement)
    {
        var rows = new List<string>();

        foreach (var rowElement in boardElement.EnumerateArray())
        {
            if (rowElement.ValueKind == JsonValueKind.String)
            {
                rows.Add(rowElement.GetString() ?? string.Empty);
                continue;
            }

            if (rowElement.ValueKind != JsonValueKind.Array)
                continue;

            var sb = new StringBuilder();
            foreach (var cell in rowElement.EnumerateArray())
            {
                var text = cell.ValueKind == JsonValueKind.String ? cell.GetString() : cell.ToString();
                sb.Append(string.IsNullOrEmpty(text) ? '.' : text[0]);
            }
            rows.Add(sb.ToString());
        }

        return rows;
    }

    private static List<ReactorBlock> ReadBlocks(JsonElement owner)
    {
        var blocks = new List<ReactorBlock>();
        if (!TryGet(owner, out var blocksElement, "blocks", "elements") || blocksElement.ValueKind != JsonValueKind.Array)
            return blocks;

        foreach (var blockElement in blocksElement.EnumerateArray())
        {
            if (blockElement.ValueKind != JsonValueKind.Object)
                continue;

            var col = ReadInt(blockElement, "col", "column", "x");
            var topRow = ReadInt(blockElement, "top_row", "topRow", "row", "y");
            if (col is null || topRow is null)
                continue;

            var direction = (ReadString(blockElement, "direction", "dir") ?? string.Empty).Trim().ToLowerInvariant() switch
            {
                "up" or "u" or "gora" or "góra" => BlockDirection.Up,
                _ => BlockDirection.Down
            };

            blocks.Add(new ReactorBlock(col.Value, topRow.Value, direction));
        }

        return blocks;
    }

    /// <summary>
    /// The robot's own coordinates, falling back to the P marker on the board when the reply omits
    /// them — the hub's answer to a command need not repeat what the grid already shows.
    /// </summary>
    private static RobotPosition? ReadRobot(JsonElement owner, List<string> rows)
    {
        if (TryGet(owner, out var playerElement, "player", "robot", "position") && playerElement.ValueKind == JsonValueKind.Object)
        {
            var col = ReadInt(playerElement, "col", "column", "x");
            var row = ReadInt(playerElement, "row", "y");
            if (col is not null && row is not null)
                return new RobotPosition(col.Value, row.Value);
        }

        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var colIndex = rows[rowIndex].IndexOf('P');
            if (colIndex >= 0)
                return new RobotPosition(colIndex + 1, rowIndex + 1);
        }

        return null;
    }

    private static bool TryGet(JsonElement owner, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (owner.TryGetProperty(name, out value) && value.ValueKind != JsonValueKind.Null)
                return true;
        }

        value = default;
        return false;
    }

    private static int? ReadInt(JsonElement owner, params string[] names) => TryGet(owner, out var value, names)
        ? value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null
        }
        : null;

    private static string? ReadString(JsonElement owner, params string[] names) =>
        TryGet(owner, out var value, names) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool ReadBool(JsonElement owner, params string[] names) => TryGet(owner, out var value, names)
        && value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
            _ => false
        };
}
