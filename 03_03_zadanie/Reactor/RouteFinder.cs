namespace _03_03_zadanie.Reactor;

/// <summary>
/// Exact answers about what is still possible. Every block travels a fixed cycle and only moves when
/// a command is sent, so the reactor repeats itself: the floor of a column is blocked or clear as a
/// function of the tick alone. That makes the whole future a graph of (column, tick) states small
/// enough to settle completely — a few dozen states — instead of guessed at one tick at a time.
/// </summary>
public sealed class RouteFinder
{
    private readonly int _columns;
    private readonly int _goalColumn;
    private readonly int _period;
    private readonly bool[][] _floorBlocked;
    private readonly bool[][] _survivable;

    public RouteFinder(BoardState board)
    {
        _columns = board.ColumnCount;
        _goalColumn = board.GoalColumn;
        _period = CycleLength(board.RowCount);

        _floorBlocked = new bool[_period][];
        for (var tick = 0; tick < _period; tick++)
        {
            var blocks = BoardProjection.After(board, tick);
            _floorBlocked[tick] = new bool[_columns + 1];
            for (var column = 1; column <= _columns; column++)
                _floorBlocked[tick][column] = blocks.Any(block => block.Covers(column, board.FloorRow));
        }

        _survivable = ComputeSurvivable();
    }

    public int GoalColumn => _goalColumn;

    /// <summary>
    /// Whether the robot standing in that column at that tick can go on living. False means the move
    /// that put it there kills it a few ticks later whatever it does next — the walls close in.
    /// </summary>
    public bool CanSurviveFrom(int column, int tick) =>
        column >= 1 && column <= _columns && _survivable[Phase(tick)][column];

    /// <summary>Fewest commands from that position to the goal column, or null when the goal is cut off.</summary>
    public int? TicksToGoalFrom(int column, int tick)
    {
        if (column == _goalColumn)
            return 0;

        var visited = new bool[_period][];
        for (var phase = 0; phase < _period; phase++)
            visited[phase] = new bool[_columns + 1];

        var queue = new Queue<(int Column, int Tick, int Steps)>();
        queue.Enqueue((column, tick, 0));
        visited[Phase(tick)][column] = true;

        while (queue.Count > 0)
        {
            var (current, currentTick, steps) = queue.Dequeue();

            foreach (var next in Reachable(current, currentTick))
            {
                if (next == _goalColumn)
                    return steps + 1;

                if (visited[Phase(currentTick + 1)][next])
                    continue;

                visited[Phase(currentTick + 1)][next] = true;
                queue.Enqueue((next, currentTick + 1, steps + 1));
            }
        }

        return null;
    }

    /// <summary>
    /// The columns the robot may occupy after one command from here: adjacent or its own, clear both
    /// on this tick and on the next. The two-tick test is the same one the guard applies, because the
    /// order in which the robot and the blocks move on the server is not documented.
    /// </summary>
    private IEnumerable<int> Reachable(int column, int tick)
    {
        for (var next = column - 1; next <= column + 1; next++)
        {
            if (next >= 1 && next <= _columns && IsFloorClear(next, tick) && IsFloorClear(next, tick + 1))
                yield return next;
        }
    }

    /// <summary>
    /// The positions from which the robot can keep moving for ever, found by starting from every
    /// position it could legally stand in and striking out those with nowhere left to go, until the
    /// set stops shrinking. What remains is exactly the set of states that are not death traps.
    /// </summary>
    private bool[][] ComputeSurvivable()
    {
        var survivable = new bool[_period][];
        for (var phase = 0; phase < _period; phase++)
        {
            survivable[phase] = new bool[_columns + 1];
            for (var column = 1; column <= _columns; column++)
                survivable[phase][column] = !_floorBlocked[phase][column];
        }

        bool changed;
        do
        {
            changed = false;
            for (var phase = 0; phase < _period; phase++)
            {
                for (var column = 1; column <= _columns; column++)
                {
                    if (!survivable[phase][column])
                        continue;

                    if (Reachable(column, phase).Any(next => survivable[Phase(phase + 1)][next]))
                        continue;

                    survivable[phase][column] = false;
                    changed = true;
                }
            }
        }
        while (changed);

        return survivable;
    }

    private bool IsFloorClear(int column, int tick) => !_floorBlocked[Phase(tick)][column];

    private int Phase(int tick) => ((tick % _period) + _period) % _period;

    /// <summary>
    /// How many ticks a block takes to return to where it started: up its travel and back down.
    /// The board repeats on that cycle, which bounds every question asked here.
    /// </summary>
    private static int CycleLength(int rowCount) => Math.Max(1, 2 * (BoardProjection.MaxTopRow(rowCount) - BoardProjection.MinTopRow));
}
