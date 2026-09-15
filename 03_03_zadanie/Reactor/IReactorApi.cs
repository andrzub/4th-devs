namespace _03_03_zadanie.Reactor;

public sealed record ReactorReply(int StatusCode, string Body, BoardState? Board)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>
    /// What the agent gets to read when no board could be parsed. The hub's own wording travels
    /// back unedited — its error messages name the exact problem and a paraphrase could only lose that.
    /// </summary>
    public string RenderRaw() => $"HTTP {StatusCode}: {Body}";
}

public sealed class ReactorBudgetExceededException(string message) : Exception(message);

/// <summary>
/// The reactor as the run can touch it. Two implementations stand behind it: the real hub and an
/// offline simulator, so the whole agent loop can be exercised without spending a robot.
/// </summary>
public interface IReactorApi
{
    string Name { get; }

    int CommandsSent { get; }

    int RemainingCommands { get; }

    /// <summary>Sends one command. This is the only thing that makes the blocks move.</summary>
    Task<ReactorReply> SendAsync(ReactorCommand command, CancellationToken cancellationToken = default);

    /// <summary>Reads the board without touching it: no tick, no cost, no risk.</summary>
    Task<ReactorReply> ReadBoardAsync(CancellationToken cancellationToken = default);
}
