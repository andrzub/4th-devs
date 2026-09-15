namespace _03_03_zadanie.Reactor;

/// <summary>
/// Where the blocks will be on the next ticks. Blocks only move when a command is sent, so one
/// command is exactly one tick and the board a command lands on is fully predictable from the
/// direction the API reports.
/// </summary>
public static class BoardProjection
{
    public static int MinTopRow => 1;

    public static int MaxTopRow(int rowCount) => rowCount - 1;

    /// <summary>
    /// Moves one block a single tick. A block that reaches the end of its travel turns around, so
    /// the direction returned here is the one that will apply to the tick after this one.
    /// </summary>
    public static ReactorBlock Advance(ReactorBlock block, int rowCount)
    {
        var maxTop = MaxTopRow(rowCount);
        var step = block.Direction == BlockDirection.Up ? -1 : 1;
        var nextTop = block.TopRow + step;

        // A direction pointing off the board contradicts the reported position; the block turns
        // around rather than leaving the reactor.
        if (nextTop < MinTopRow || nextTop > maxTop)
            return block with { Direction = Flip(block.Direction) };

        var nextDirection = nextTop == MinTopRow ? BlockDirection.Down : nextTop == maxTop ? BlockDirection.Up : block.Direction;
        return block with { TopRow = nextTop, Direction = nextDirection };
    }

    public static IReadOnlyList<ReactorBlock> AdvanceAll(IReadOnlyList<ReactorBlock> blocks, int rowCount) =>
        [.. blocks.Select(block => Advance(block, rowCount))];

    /// <summary>Block positions after <paramref name="ticks"/> commands, the current state being tick 0.</summary>
    public static IReadOnlyList<ReactorBlock> After(BoardState board, int ticks)
    {
        var blocks = board.Blocks;
        for (var tick = 0; tick < ticks; tick++)
            blocks = AdvanceAll(blocks, board.RowCount);

        return blocks;
    }

    /// <summary>
    /// How many ticks until a block covers the floor of that column, or null when it stays clear for
    /// the whole horizon. Zero means the floor is blocked right now.
    /// </summary>
    public static int? TicksUntilFloorBlocked(BoardState board, int col, int horizon = 12)
    {
        for (var tick = 0; tick <= horizon; tick++)
        {
            if (After(board, tick).Any(block => block.Covers(col, board.FloorRow)))
                return tick;
        }

        return null;
    }

    private static BlockDirection Flip(BlockDirection direction) =>
        direction == BlockDirection.Up ? BlockDirection.Down : BlockDirection.Up;
}
