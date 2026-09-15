using System.Text;

namespace _03_03_zadanie.Reactor;

public enum BlockDirection { Up, Down }

/// <summary>
/// One reactor block. It is two cells tall, so it covers <see cref="TopRow"/> and the row below it.
/// <see cref="Direction"/> is where the block travels on the next tick, not where it came from.
/// </summary>
public sealed record ReactorBlock(int Col, int TopRow, BlockDirection Direction)
{
    public int BottomRow => TopRow + 1;

    public bool Covers(int col, int row) => col == Col && row >= TopRow && row <= BottomRow;

    public string Arrow => Direction == BlockDirection.Up ? "^" : "v";
}

public sealed record RobotPosition(int Col, int Row);

/// <summary>
/// The board exactly as the API reported it. Rows are numbered from the top, columns from the left,
/// both one-based, matching every coordinate the API and the task description use.
/// </summary>
public sealed class BoardState
{
    public required IReadOnlyList<string> Rows { get; init; }
    public required IReadOnlyList<ReactorBlock> Blocks { get; init; }
    public RobotPosition? Robot { get; init; }
    public bool ReachedGoal { get; init; }
    public bool IsCrushed { get; init; }
    public string? Message { get; init; }

    public int RowCount => Rows.Count;
    public int ColumnCount => Rows.Count == 0 ? 0 : Rows[0].Length;

    /// <summary>The bottom row, which is the only row the robot ever stands on.</summary>
    public int FloorRow => RowCount;

    public char CellAt(int col, int row) =>
        row >= 1 && row <= RowCount && col >= 1 && col <= Rows[row - 1].Length ? Rows[row - 1][col - 1] : '?';

    /// <summary>Where the cooling module has to be delivered, read off the G marker rather than assumed.</summary>
    public int GoalColumn
    {
        get
        {
            var marked = Rows.Count == 0 ? -1 : Rows[FloorRow - 1].IndexOf('G');
            return marked >= 0 ? marked + 1 : ColumnCount;
        }
    }

    public ReactorBlock? BlockInColumn(int col) => Blocks.FirstOrDefault(b => b.Col == col);

    /// <summary>True when a block already covers the floor cell of that column, which is lethal on contact.</summary>
    public bool IsFloorBlocked(int col) => Blocks.Any(b => b.Covers(col, FloorRow));

    /// <summary>
    /// The board drawn back as text, with each block carrying the direction of its next move.
    /// This is what the agent reads, so the arrow travels with the cell rather than in a side list.
    /// </summary>
    public string Render()
    {
        var sb = new StringBuilder();
        sb.Append("     ");
        for (var col = 1; col <= ColumnCount; col++)
            sb.Append($" c{col} ");
        sb.AppendLine();

        for (var row = 1; row <= RowCount; row++)
        {
            sb.Append($"  r{row} ");
            for (var col = 1; col <= ColumnCount; col++)
            {
                var cell = CellAt(col, row);
                var block = Blocks.FirstOrDefault(b => b.Covers(col, row));
                sb.Append(block is null ? $"  {cell} " : $" {cell}{block.Arrow} ");
            }
            sb.AppendLine();
        }

        sb.AppendLine($"  robot: {(Robot is null ? "unknown" : $"column {Robot.Col}, row {Robot.Row}")}   ^ = block moves up next tick, v = down");
        return sb.ToString().TrimEnd();
    }
}
