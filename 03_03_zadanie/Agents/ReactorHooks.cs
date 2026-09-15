using _03_03_zadanie.Llm;
using _03_03_zadanie.Mission;
using _03_03_zadanie.Reactor;

namespace _03_03_zadanie.Agents;

/// <summary>
/// The run's three interventions in the agent's loop: a command is screened before it can reach the
/// reactor, every tool result comes back with the context needed for the next decision, and the
/// agent cannot declare the job done while the robot is still short of its slot.
/// </summary>
public sealed class ReactorHooks(ReactorSession session, MissionState mission, int maxResets) : IAgentHooks
{
    public const string SendCommandTool = "send_command";

    public Task<string?> BeforeToolCallAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        if (call.FunctionName != SendCommandTool)
            return Task.FromResult<string?>(null);

        var requested = Tools.ToolArguments.Parse(call.ArgumentsJson).GetString("command");
        if (!ReactorCommands.TryParse(requested, out var command))
            return Task.FromResult<string?>($"'{requested}' is not a command the reactor knows. Valid commands: {string.Join(", ", ReactorCommands.All)}.");

        return Task.FromResult(session.Screen(command));
    }

    public Task<string> AfterToolResultAsync(ToolCall call, string result, CancellationToken cancellationToken = default)
    {
        var notes = new List<string>();

        if (mission.FlagReceived)
            notes.Add("The flag has arrived, so the mission is over. Stop sending commands and report it.");

        if (session.Crushed)
            notes.Add("The robot is destroyed. Nothing but 'reset' changes that, and a reset means the crossing starts over.");

        if (PositionIsLost)
            notes.Add(ResetsLeft > 0
                ? "The position is lost. Send 'reset' — you do not need to ask, the report above is the authorisation — and cross again, this time waiting at the start until the blocks line up."
                : "The position is lost and the reset allowance is spent. Report what happened and stop.");

        if (LastCommandsWereRefused(2))
            notes.Add("Two commands in a row were refused. The situation report above already lists the commands that survive this tick — take one of those instead of guessing again.");

        return Task.FromResult(notes.Count == 0 ? result : $"{result}{Environment.NewLine}{Environment.NewLine}{string.Join(Environment.NewLine, notes)}");
    }

    public Task<string?> BeforeFinishAsync(CancellationToken cancellationToken = default)
    {
        if (session.GoalReached || session.Crushed || session.RemainingCommands <= 0)
            return Task.FromResult<string?>(null);

        // Demanding more work from a robot that has nowhere to step is how the agent ends up asking
        // the same question three times. A lost position has exactly one answer, so the hook gives it.
        if (PositionIsLost)
            return Task.FromResult(ResetsLeft > 0
                ? "The position is lost and 'reset' is authorised. Send it now, then cross again — hold at the starting column until the blocks are somewhere the crossing can be finished."
                : null);

        var column = session.Board?.Robot?.Col;
        var where = column is null ? "The robot has not been placed on the board yet." : $"The robot is still in column {column}, short of the goal.";
        return Task.FromResult<string?>($"{where} The cooling module is not installed, so the job is not done. Look at the board and take the next survivable step.");
    }

    private bool PositionIsLost => session.Board is { IsCrushed: false, ReachedGoal: false } board && MoveGuard.SurvivableMoves(board).Count == 0;

    private int ResetsLeft => maxResets - mission.History.Count(record => record.Sent && record.Command == ReactorCommand.Reset.Name());

    private bool LastCommandsWereRefused(int count) =>
        mission.History.Count >= count && mission.History.TakeLast(count).All(record => !record.Sent);
}
