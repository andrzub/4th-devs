using System.Text;

namespace _03_03_zadanie.Reactor;

/// <summary>
/// Every way the run could lose a robot, checked offline. The reactor costs a spare robot per
/// mistake and the hub only says so afterwards, so the rules that decide a move are verified here
/// against hand-built boards — no network, no API key, no ticks spent.
/// </summary>
public static class MechanicsTestSuite
{
    private const int Rows = 5;
    private const int Columns = 7;

    public static bool Run()
    {
        var results = new List<(string Case, bool Passed, string Detail)>();

        void Expect(string name, bool passed, string detail = "") => results.Add((name, passed, detail));

        void ExpectVerdict(string name, ReactorCommand command, BoardState? board, bool started, bool allowed)
        {
            var verdict = MoveGuard.Evaluate(command, board, started);
            Expect(name, verdict.Allowed == allowed, verdict.Reason);
        }

        // --- the floor is already sealed -------------------------------------------------------
        ExpectVerdict("right into a column sealed right now", ReactorCommand.Right, Board(3, Block(4, 4, BlockDirection.Up)), true, false);
        ExpectVerdict("left into a column sealed right now", ReactorCommand.Left, Board(3, Block(2, 4, BlockDirection.Up)), true, false);
        ExpectVerdict("wait while a block already covers the robot's column", ReactorCommand.Wait, Board(3, Block(3, 4, BlockDirection.Up)), true, false);

        // --- the floor closes on the very tick the command spends ------------------------------
        ExpectVerdict("right into a column that closes on this tick", ReactorCommand.Right, Board(3, Block(4, 3, BlockDirection.Down)), true, false);
        ExpectVerdict("wait while the robot's own column closes on this tick", ReactorCommand.Wait, Board(3, Block(3, 3, BlockDirection.Down)), true, false);
        ExpectVerdict("left into a column that closes on this tick", ReactorCommand.Left, Board(3, Block(2, 3, BlockDirection.Down)), true, false);

        // --- a block one row away but travelling away is not a threat --------------------------
        ExpectVerdict("right past a block that is rising", ReactorCommand.Right, Board(3, Block(4, 3, BlockDirection.Up)), true, true);
        ExpectVerdict("right into an empty column", ReactorCommand.Right, Board(3, Block(6, 1, BlockDirection.Down)), true, true);
        ExpectVerdict("wait under a block that is rising", ReactorCommand.Wait, Board(3, Block(3, 3, BlockDirection.Up)), true, true);

        // --- board edges -----------------------------------------------------------------------
        ExpectVerdict("left from the first column", ReactorCommand.Left, Board(1), true, false);
        ExpectVerdict("right from the last column", ReactorCommand.Right, Board(Columns), true, false);

        // --- run lifecycle ---------------------------------------------------------------------
        ExpectVerdict("start before anything is running", ReactorCommand.Start, null, false, true);
        ExpectVerdict("start again mid-run", ReactorCommand.Start, Board(3), true, false);
        ExpectVerdict("move before the run is started", ReactorCommand.Right, null, false, false);
        ExpectVerdict("move with no board in hand", ReactorCommand.Right, null, true, false);
        ExpectVerdict("reset is never blocked", ReactorCommand.Reset, Board(3, Block(3, 4, BlockDirection.Up)), true, true);

        // --- terminal states -------------------------------------------------------------------
        ExpectVerdict("move after the robot is crushed", ReactorCommand.Right, Board(3, crushed: true), true, false);
        ExpectVerdict("move after the goal is reached", ReactorCommand.Right, Board(7, reachedGoal: true), true, false);
        ExpectVerdict("move when the robot is reported off the floor", ReactorCommand.Right, Board(3, robotRow: 4), true, false);

        // --- what is left when the neighbourhood closes ----------------------------------------
        var boxedIn = Board(3, Block(2, 3, BlockDirection.Down), Block(3, 3, BlockDirection.Down), Block(4, 3, BlockDirection.Down));
        Expect("no survivable move when every neighbouring column closes", MoveGuard.SurvivableMoves(boxedIn).Count == 0);
        Expect("advice says the position is lost", BoardAdvice.Describe(boxedIn).Contains("only 'reset' remains"));

        var rightCloses = Board(3, Block(4, 3, BlockDirection.Down));
        Expect("survivable moves exclude only the closing column",
            MoveGuard.SurvivableMoves(rightCloses).SequenceEqual([ReactorCommand.Left, ReactorCommand.Wait]));

        // --- block travel ----------------------------------------------------------------------
        Expect("a block turns around at the top", BoardProjection.Advance(Block(2, 2, BlockDirection.Up), Rows) == Block(2, 1, BlockDirection.Down));
        Expect("a block turns around at the bottom", BoardProjection.Advance(Block(2, 3, BlockDirection.Down), Rows) == Block(2, 4, BlockDirection.Up));
        Expect("a direction pointing off the board turns around in place", BoardProjection.Advance(Block(2, 1, BlockDirection.Up), Rows) == Block(2, 1, BlockDirection.Down));
        Expect("a block mid-travel keeps its direction", BoardProjection.Advance(Block(2, 2, BlockDirection.Down), Rows) == Block(2, 3, BlockDirection.Down));

        var timing = Board(3, Block(5, 1, BlockDirection.Down));
        Expect("time to a sealed floor is counted in ticks", BoardProjection.TicksUntilFloorBlocked(timing, 5) == 3, $"got {BoardProjection.TicksUntilFloorBlocked(timing, 5)}");
        Expect("an empty column never seals", BoardProjection.TicksUntilFloorBlocked(timing, 6) is null);

        // --- reading the API's answer ----------------------------------------------------------
        var preview = "{\"board\":[[\".\",\".\",\".\",\".\",\".\",\".\",\".\"],[\".\",\".\",\".\",\".\",\".\",\".\",\".\"],[\".\",\"B\",\".\",\".\",\".\",\".\",\".\"],[\".\",\"B\",\".\",\".\",\".\",\".\",\".\"],[\"P\",\".\",\".\",\".\",\".\",\".\",\"G\"]],"
            + "\"blocks\":[{\"col\":2,\"top_row\":3,\"direction\":\"down\"}],\"player\":{\"col\":1,\"row\":5},\"reached_goal\":false,\"is_crushed\":false,\"message\":\"ok\"}";
        var parsed = BoardStateParser.TryParse(preview);
        Expect("a preview reply parses", parsed is { RowCount: Rows, ColumnCount: Columns });
        Expect("blocks carry their direction", parsed?.Blocks is [{ Col: 2, TopRow: 3, Direction: BlockDirection.Down }]);
        Expect("the robot is located", parsed?.Robot == new RobotPosition(1, 5));
        Expect("the goal column is read off the board", parsed?.GoalColumn == 7);

        var nested = "{\"code\":0,\"message\":{\"state\":{\"board\":[\".......\",\".......\",\"..B....\",\"..B....\",\"P.....G\"],\"blocks\":[{\"col\":3,\"topRow\":3,\"dir\":\"up\"}]}}}";
        var nestedBoard = BoardStateParser.TryParse(nested);
        Expect("a board nested anywhere in the reply is found", nestedBoard?.Blocks is [{ Col: 3, Direction: BlockDirection.Up }]);
        Expect("rows written as plain strings parse", nestedBoard?.Rows.Count == 5);
        Expect("the robot falls back to the P marker", nestedBoard?.Robot == new RobotPosition(1, 5));

        Expect("a reply with no board at all reads as none", BoardStateParser.TryParse("{\"code\":-980,\"message\":\"No reactor state found\"}") is null);

        // --- the reactor as it actually was, tick by tick, on the run that lost a robot -----------
        // Columns 2, 3 and 4 travel in lockstep, so they seal the floor together and a robot standing
        // among them has nowhere to step. The trap is invisible one tick ahead and fatal two ticks on.
        var opening = Board(1, Block(2, 1, BlockDirection.Down), Block(3, 1, BlockDirection.Down), Block(4, 1, BlockDirection.Down), Block(5, 4, BlockDirection.Up), Block(6, 3, BlockDirection.Up));
        var afterOneStep = Board(2, Block(2, 2, BlockDirection.Down), Block(3, 2, BlockDirection.Down), Block(4, 2, BlockDirection.Down), Block(5, 3, BlockDirection.Up), Block(6, 2, BlockDirection.Up));
        var afterTwoSteps = Board(3, Block(2, 3, BlockDirection.Down), Block(3, 3, BlockDirection.Down), Block(4, 3, BlockDirection.Down), Block(5, 2, BlockDirection.Up), Block(6, 1, BlockDirection.Down));

        Expect("the real board's second step is predicted from the first", Renders(BoardProjection.After(opening, 1)) == Renders(afterOneStep.Blocks));
        Expect("the real board's third step is predicted from the first", Renders(BoardProjection.After(opening, 2)) == Renders(afterTwoSteps.Blocks));

        ExpectVerdict("stepping into the lockstep wall is refused as a dead end", ReactorCommand.Right, afterOneStep, true, false);
        ExpectVerdict("retreating from the lockstep wall is allowed", ReactorCommand.Left, afterOneStep, true, true);
        ExpectVerdict("holding beside the lockstep wall is allowed", ReactorCommand.Wait, afterOneStep, true, true);
        Expect("the position the run actually reached has no survivable move", MoveGuard.SurvivableMoves(afterTwoSteps).Count == 0);

        // Three lockstep blocks ending at the goal make the goal column a dead end, but arriving there
        // ends the mission, so the winning move must not be refused for what happens afterwards.
        var goalIsATrap = Board(6, Block(5, 1, BlockDirection.Down), Block(6, 1, BlockDirection.Down), Block(7, 1, BlockDirection.Down));
        Expect("the goal column is a dead end in this arrangement", !new RouteFinder(goalIsATrap).CanSurviveFrom(7, 1));
        ExpectVerdict("stepping onto the goal is allowed even so", ReactorCommand.Right, goalIsATrap, true, true);

        var openingRoutes = new RouteFinder(opening);
        var ticksToGoal = openingRoutes.TicksToGoalFrom(1, 0);
        Expect("the real board can be crossed from the start", ticksToGoal is not null, "no route found");
        Expect("crossing it takes ten commands", ticksToGoal == 10, $"got {ticksToGoal}");
        Expect("the starting column is never a trap", openingRoutes.CanSurviveFrom(1, 0));

        return Report(results);
    }

    private static ReactorBlock Block(int col, int topRow, BlockDirection direction) => new(col, topRow, direction);

    private static string Renders(IEnumerable<ReactorBlock> blocks) =>
        string.Join(" ", blocks.OrderBy(block => block.Col).Select(block => $"{block.Col}:{block.TopRow}{block.Arrow}"));

    /// <summary>Builds the grid the API would have drawn for this arrangement, so the cases describe positions rather than ASCII art.</summary>
    private static BoardState Board(int robotColumn, params ReactorBlock[] blocks) => Board(robotColumn, Rows, false, false, blocks);

    private static BoardState Board(int robotColumn, bool crushed = false, bool reachedGoal = false, int robotRow = Rows) =>
        Board(robotColumn, robotRow, crushed, reachedGoal, []);

    private static BoardState Board(int robotColumn, int robotRow, bool crushed, bool reachedGoal, ReactorBlock[] blocks)
    {
        var rows = new List<string>();
        for (var row = 1; row <= Rows; row++)
        {
            var sb = new StringBuilder();
            for (var col = 1; col <= Columns; col++)
            {
                sb.Append(blocks.Any(block => block.Covers(col, row)) ? 'B'
                    : row == Rows && col == robotColumn ? 'P'
                    : row == Rows && col == Columns ? 'G'
                    : '.');
            }
            rows.Add(sb.ToString());
        }

        return new BoardState
        {
            Rows = rows,
            Blocks = blocks,
            Robot = new RobotPosition(robotColumn, robotRow),
            IsCrushed = crushed,
            ReachedGoal = reachedGoal
        };
    }

    private static bool Report(List<(string Case, bool Passed, string Detail)> results)
    {
        foreach (var (name, passed, detail) in results)
        {
            Console.WriteLine($"  {(passed ? "ok  " : "FAIL")}  {name}");
            if (!passed && detail.Length > 0)
                Console.WriteLine($"          {detail}");
        }

        var failed = results.Count(result => !result.Passed);
        Console.WriteLine();
        Console.WriteLine(failed == 0 ? $"All {results.Count} cases passed." : $"{failed} of {results.Count} cases FAILED.");
        return failed == 0;
    }
}
