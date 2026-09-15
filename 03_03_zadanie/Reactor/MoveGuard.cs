namespace _03_03_zadanie.Reactor;

public sealed record MoveVerdict(bool Allowed, string Reason)
{
    public static MoveVerdict Allow(string reason) => new(true, reason);
    public static MoveVerdict Deny(string reason) => new(false, reason);
}

/// <summary>
/// Decides whether a command is survivable before it is sent. A command rejected here costs one
/// agent turn; a command that crushes the robot costs the whole run and a spare robot, so the
/// geometry is settled in code rather than left to the model's reading of the board.
/// </summary>
public static class MoveGuard
{
    public static MoveVerdict Evaluate(ReactorCommand command, BoardState? board, bool runStarted)
    {
        if (command == ReactorCommand.Start)
            return runStarted
                ? MoveVerdict.Deny("The run is already open. Starting again would throw away the robot's progress.")
                : MoveVerdict.Allow("Opens the run.");

        if (command == ReactorCommand.Reset)
            return MoveVerdict.Allow("Reset returns the robot to the start, so it is never blocked here.");

        if (!runStarted)
            return MoveVerdict.Deny("Nothing is running yet. Send 'start' before moving the robot.");

        if (board is null)
            return MoveVerdict.Deny("The board is unknown, so no move can be judged safe. Read the board first.");

        if (board.IsCrushed)
            return MoveVerdict.Deny("The robot has been crushed. Only 'reset' does anything now.");

        if (board.ReachedGoal)
            return MoveVerdict.Deny("The robot already stands on the goal. The mission is over.");

        if (board.Robot is null)
            return MoveVerdict.Deny("The board does not say where the robot is, so no move can be judged safe.");

        var floor = board.FloorRow;
        if (board.Robot.Row != floor)
            return MoveVerdict.Deny($"The robot is reported on row {board.Robot.Row} instead of the floor (row {floor}). The board is not understood, so nothing is sent.");

        var target = board.Robot.Col + command.ColumnStep();
        if (target < 1)
            return MoveVerdict.Deny("The robot stands in the first column; there is nothing to the left of it.");

        if (target > board.ColumnCount)
            return MoveVerdict.Deny($"The robot stands in the last column ({board.ColumnCount}); there is nothing to the right of it.");

        var standing = board.Blocks.FirstOrDefault(block => block.Covers(target, floor));
        if (standing is not null)
            return MoveVerdict.Deny($"Column {target} is sealed right now: the block there covers rows {standing.TopRow}-{standing.BottomRow}, the floor included.");

        // The board also ticks on this command, so the cell has to be clear after the blocks move.
        // Checking both the current and the next tick keeps the verdict right whichever of the two
        // happens first on the server.
        var landing = BoardProjection.After(board, 1).FirstOrDefault(block => block.Covers(target, floor));
        if (landing is not null)
            return MoveVerdict.Deny($"Column {target} closes on this very tick: its block sits at rows {board.BlockInColumn(target)?.TopRow}-{board.BlockInColumn(target)?.BottomRow} moving {board.BlockInColumn(target)?.Direction.ToString().ToLowerInvariant()} and lands on rows {landing.TopRow}-{landing.BottomRow}.");

        // Surviving this tick is not enough. Blocks in neighbouring columns can travel in lockstep and
        // close a whole stretch of the floor at once, so a column that is clear today can be a cell the
        // robot cannot leave tomorrow. That is still a question of destroying the robot, not of tactics,
        // and the block motion is deterministic enough to answer it exactly.
        // Stepping onto the goal ends the mission, so what the reactor does afterwards cannot make
        // that move a trap.
        if (target != board.GoalColumn && !new RouteFinder(board).CanSurviveFrom(target, 1))
            return MoveVerdict.Deny($"Column {target} is clear this tick but it is a dead end: from there every later move runs under a block. The robot would be crushed a few ticks from now whatever it did.");

        return MoveVerdict.Allow($"Column {target} stays clear through this tick and leaves the robot somewhere it can go on living.");
    }

    /// <summary>
    /// The movement commands that survive the tick. An empty result means the position is lost and
    /// only a reset is left — the guard says so instead of silently blocking every option.
    /// </summary>
    public static IReadOnlyList<ReactorCommand> SurvivableMoves(BoardState board)
    {
        ReactorCommand[] moves = [ReactorCommand.Left, ReactorCommand.Wait, ReactorCommand.Right];
        return [.. moves.Where(move => Evaluate(move, board, runStarted: true).Allowed)];
    }
}
