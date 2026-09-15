using System.Text.Json.Nodes;

namespace _03_03_zadanie.Reactor;

/// <summary>
/// An offline reactor built to the rules in the briefing. It answers in the same JSON shape as the
/// preview endpoint, so the parser, the guard and the agent loop all run against it unchanged — a
/// full rehearsal that costs neither a request nor a robot.
/// </summary>
public sealed class SimulatedReactorApi : IReactorApi
{
    private const int Rows = 5;
    private const int Columns = 7;
    private const int GoalColumn = 7;

    private readonly int _seed;
    private List<ReactorBlock> _blocks = [];
    private int _robotColumn = 1;
    private bool _started;
    private bool _crushed;
    private string? _message;

    public SimulatedReactorApi(int seed = 0)
    {
        _seed = seed;
        Reset();
    }

    public string Name => "simulator";

    public int CommandsSent { get; private set; }

    public int RemainingCommands => int.MaxValue;

    public bool ReachedGoal => _robotColumn == GoalColumn && !_crushed;

    public bool Crushed => _crushed;

    public Task<ReactorReply> ReadBoardAsync(CancellationToken cancellationToken = default) =>
        _started ? Task.FromResult(Reply(null)) : Task.FromResult(NotStarted());

    public Task<ReactorReply> SendAsync(ReactorCommand command, CancellationToken cancellationToken = default)
    {
        CommandsSent++;

        switch (command)
        {
            case ReactorCommand.Reset:
                Reset();
                _started = true;
                return Task.FromResult(Reply("Reactor reset. The robot is back at the start."));

            case ReactorCommand.Start when _started:
                return Task.FromResult(Reply("The task is already running."));

            case ReactorCommand.Start:
                _started = true;
                return Task.FromResult(Reply("Task started."));
        }

        if (!_started)
            return Task.FromResult(NotStarted());

        if (_crushed || ReachedGoal)
            return Task.FromResult(Reply(_crushed ? "The robot is destroyed." : "The robot already stands on the goal."));

        _robotColumn = Math.Clamp(_robotColumn + command.ColumnStep(), 1, Columns);
        _blocks = [.. BoardProjection.AdvanceAll(_blocks, Rows)];

        if (_blocks.Any(block => block.Covers(_robotColumn, Rows)))
        {
            _crushed = true;
            _message = "The robot was crushed by a reactor block.";
            return Task.FromResult(Reply(_message));
        }

        _message = ReachedGoal ? "The cooling module reached its slot."
            : command == ReactorCommand.Wait ? $"Robot held position in column {_robotColumn}."
            : $"Robot moved to column {_robotColumn}.";
        return Task.FromResult(Reply(_message));
    }

    /// <summary>Before the task is started the reactor has no state at all, exactly as the hub reports it.</summary>
    private static ReactorReply NotStarted() => new(404, "{\"code\":-980,\"message\":\"No reactor state found for this API key. Start the task first.\"}", null);

    private void Reset()
    {
        _robotColumn = 1;
        _crushed = false;
        _message = null;

        // Columns 2-6 each carry a block; the phase offsets come from the seed so a run can be
        // rehearsed against several arrangements without the layout ever being random mid-run.
        _blocks = [];
        for (var col = 2; col <= 6; col++)
        {
            var phase = (col * 2 + _seed) % 6;
            var topRow = phase < 3 ? 1 + phase : 4 - (phase - 3);
            var direction = phase < 3 ? BlockDirection.Down : BlockDirection.Up;
            _blocks.Add(new ReactorBlock(col, Math.Clamp(topRow, 1, Rows - 1), direction));
        }
    }

    /// <summary>Serialises the state the way the preview endpoint does, so nothing downstream can tell the difference.</summary>
    private ReactorReply Reply(string? message)
    {
        var board = new JsonArray();
        for (var row = 1; row <= Rows; row++)
        {
            var rowArray = new JsonArray();
            for (var col = 1; col <= Columns; col++)
            {
                var cell = _blocks.Any(block => block.Covers(col, row)) ? "B"
                    : row == Rows && col == _robotColumn ? "P"
                    : row == Rows && col == GoalColumn ? "G"
                    : ".";
                rowArray.Add(cell);
            }
            board.Add(rowArray);
        }

        var blocks = new JsonArray();
        foreach (var block in _blocks)
        {
            blocks.Add(new JsonObject
            {
                ["col"] = block.Col,
                ["top_row"] = block.TopRow,
                ["direction"] = block.Direction.ToString().ToLowerInvariant()
            });
        }

        var payload = new JsonObject
        {
            ["board"] = board,
            ["blocks"] = blocks,
            ["player"] = new JsonObject { ["col"] = _robotColumn, ["row"] = Rows },
            ["reached_goal"] = ReachedGoal,
            ["is_crushed"] = _crushed,
            ["message"] = message ?? _message,
            ["updated_at"] = DateTimeOffset.Now.ToString("O")
        };

        var body = payload.ToJsonString();
        return new ReactorReply(200, body, BoardStateParser.TryParse(body));
    }
}
