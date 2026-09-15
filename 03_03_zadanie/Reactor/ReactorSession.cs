using _03_03_zadanie.Mission;

namespace _03_03_zadanie.Reactor;

/// <summary>
/// The run's picture of the reactor and the only road to it. Every command passes the guard here as
/// well as in the agent's hook: the hook gives the model a cheap, explained refusal, while this call
/// protects the manual mode that has no hooks at all.
/// </summary>
public sealed class ReactorSession(IReactorApi api, MissionState mission)
{
    public BoardState? Board { get; private set; }

    public bool Started { get; private set; }

    public int CommandsSent => api.CommandsSent;

    public int RemainingCommands => api.RemainingCommands;

    public bool GoalReached => Board is { ReachedGoal: true } || mission.FlagReceived;

    public bool Crushed => Board is { IsCrushed: true };

    /// <summary>Reads the board without sending a command, so the blocks do not move and no budget is spent.</summary>
    public async Task<string> LookAsync(CancellationToken cancellationToken = default)
    {
        var reply = await api.ReadBoardAsync(cancellationToken);
        if (reply.Board is null)
            return $"The reactor did not return a board. {reply.RenderRaw()}";

        // A board only exists once the task has been started, so its arrival settles that
        // question for a process that did not send the start command itself.
        Board = reply.Board;
        Started = true;
        mission.ScanForFlag(reply.Body);
        return Situation();
    }

    /// <summary>
    /// The guard's verdict written the way the agent will read it, or null when the command may be
    /// sent. Both the hook and <see cref="SendAsync"/> go through here, so the manual mode and the
    /// agent are held to exactly the same rules.
    /// </summary>
    public string? Screen(ReactorCommand command)
    {
        var verdict = MoveGuard.Evaluate(command, Board, Started);
        if (verdict.Allowed)
            return null;

        mission.Record(command.Name(), sent: false, verdict.Reason);
        return $"REFUSED before sending: {verdict.Reason}{Environment.NewLine}{Environment.NewLine}{Situation()}";
    }

    public async Task<string> SendAsync(ReactorCommand command, CancellationToken cancellationToken = default)
    {
        if (Screen(command) is { } refusal)
            return refusal;

        var reply = await api.SendAsync(command, cancellationToken);
        mission.ScanForFlag(reply.Body);

        if (command is ReactorCommand.Start or ReactorCommand.Reset)
            Started = true;

        // The answer to a command need not carry the board; the free read fills that gap so the
        // guard is never left judging the next move on a stale picture.
        Board = reply.Board ?? (await api.ReadBoardAsync(cancellationToken)).Board ?? Board;

        var outcome = reply.Board is null && Board is null ? reply.RenderRaw() : Headline(reply);
        mission.Record(command.Name(), sent: true, outcome);

        return $"{outcome}{Environment.NewLine}{Environment.NewLine}{Situation()}";
    }

    private string Headline(ReactorReply reply)
    {
        if (mission.FlagReceived)
            return $"HTTP {reply.StatusCode}. The reactor answered with the mission flag.";

        if (Crushed)
            return $"HTTP {reply.StatusCode}. The robot has been crushed.";

        if (GoalReached)
            return $"HTTP {reply.StatusCode}. The robot stands on the goal.";

        var message = Board?.Message;
        return string.IsNullOrWhiteSpace(message) ? $"HTTP {reply.StatusCode}. Command accepted." : $"HTTP {reply.StatusCode}. {message}";
    }

    private string Situation()
    {
        if (Board is null)
            return "No board is known yet.";

        var budget = RemainingCommands == int.MaxValue
            ? $"Commands sent: {CommandsSent}."
            : $"Commands sent: {CommandsSent}, {RemainingCommands} left in the budget.";

        return $"{BoardAdvice.Describe(Board)}{Environment.NewLine}{budget}";
    }
}
