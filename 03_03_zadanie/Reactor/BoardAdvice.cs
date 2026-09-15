using System.Text;

namespace _03_03_zadanie.Reactor;

/// <summary>
/// Turns a board into the situation report the agent reads after every command. The raw JSON says
/// where the blocks are; this says what that means for the next move, which is the part the model
/// would otherwise have to re-derive — and occasionally get wrong — on every single tick.
/// </summary>
public static class BoardAdvice
{
    public static string Describe(BoardState board)
    {
        var sb = new StringBuilder();
        sb.AppendLine(board.Render());
        sb.AppendLine();

        if (board.IsCrushed)
        {
            sb.AppendLine("STATUS: the robot has been crushed. Nothing but 'reset' will change that.");
            return sb.ToString().TrimEnd();
        }

        if (board.ReachedGoal)
        {
            sb.AppendLine("STATUS: the robot stands on the goal. The cooling module is delivered.");
            return sb.ToString().TrimEnd();
        }

        if (board.Robot is null)
        {
            sb.AppendLine("STATUS: the board does not say where the robot is.");
            return sb.ToString().TrimEnd();
        }

        var col = board.Robot.Col;
        var goalColumn = board.GoalColumn;
        sb.AppendLine($"STATUS: robot in column {col}, {Math.Max(0, goalColumn - col)} column(s) short of the goal in column {goalColumn}.");
        sb.AppendLine();
        sb.AppendLine("Columns within reach:");

        var routes = new RouteFinder(board);
        foreach (var (candidate, command, label) in Neighbourhood(col, board.ColumnCount))
        {
            var allowed = MoveGuard.Evaluate(command, board, runStarted: true).Allowed;
            sb.AppendLine($"  {label,-22} {DescribeColumn(board, candidate)}{(allowed ? DescribeRoute(routes, candidate) : "; REFUSED, this move would destroy the robot")}");
        }

        sb.AppendLine();
        sb.AppendLine(DescribeOptions(board));
        return sb.ToString().TrimEnd();
    }

    private static IEnumerable<(int Column, ReactorCommand Command, string Label)> Neighbourhood(int col, int columnCount)
    {
        if (col - 1 >= 1)
            yield return (col - 1, ReactorCommand.Left, $"left  -> column {col - 1}");

        yield return (col, ReactorCommand.Wait, $"wait  -> column {col}");

        if (col + 1 <= columnCount)
            yield return (col + 1, ReactorCommand.Right, $"right -> column {col + 1}");
    }

    private static string DescribeColumn(BoardState board, int col)
    {
        var block = board.BlockInColumn(col);
        if (block is null)
            return "no block in this column, safe indefinitely";

        var position = $"block at rows {block.TopRow}-{block.BottomRow} moving {block.Direction.ToString().ToLowerInvariant()}";
        var ticks = BoardProjection.TicksUntilFloorBlocked(board, col);

        return ticks switch
        {
            null => $"{position}; never reaches the floor",
            0 => $"{position}; the floor is sealed RIGHT NOW",
            1 => $"{position}; seals the floor on the NEXT tick",
            _ => $"{position}; seals the floor in {ticks} ticks"
        };
    }

    /// <summary>
    /// What that column is worth once the robot is standing in it: whether the crossing can still be
    /// finished from there, and how many commands it would take at best. The decision stays with the
    /// agent — this only spares it from re-deriving the block cycles on every tick.
    /// </summary>
    private static string DescribeRoute(RouteFinder routes, int col)
    {
        if (col == routes.GoalColumn)
            return "; THIS IS THE GOAL — stepping here finishes the mission";

        var ticks = routes.TicksToGoalFrom(col, 1);
        return ticks is null ? "; survivable, but the goal cannot be reached from there" : $"; goal reachable in {ticks} more command(s)";
    }

    private static string DescribeOptions(BoardState board)
    {
        var survivable = MoveGuard.SurvivableMoves(board);

        if (survivable.Count == 0)
            return "SURVIVABLE COMMANDS: none. Every neighbouring column and the robot's own column close on this tick — the position is lost and only 'reset' remains.";

        return $"SURVIVABLE COMMANDS: {string.Join(", ", survivable.Select(move => move.Name()))}. Anything else is refused before it is sent.";
    }
}
