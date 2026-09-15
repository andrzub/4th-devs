namespace _03_03_zadanie.Reactor;

/// <summary>
/// A deterministic pilot used only to rehearse the mechanics offline: it always takes the first
/// survivable command from right, wait, left. It is not the solution — the agent is — but it proves
/// on the simulator that the guard leaves a way across instead of blocking the robot into a corner.
/// </summary>
public static class RehearsalPilot
{
    public static async Task<bool> CrossAsync(IReactorApi api, int maxCommands = 100, bool verbose = true)
    {
        var reply = await api.SendAsync(ReactorCommand.Start);
        var board = reply.Board;

        for (var step = 1; step <= maxCommands; step++)
        {
            if (board is null)
            {
                Console.WriteLine($"  step {step}: no board in the reply — {reply.RenderRaw()}");
                return false;
            }

            if (board.ReachedGoal)
            {
                Console.WriteLine($"  goal reached after {api.CommandsSent} commands.");
                return true;
            }

            if (board.IsCrushed)
            {
                Console.WriteLine($"  robot crushed after {api.CommandsSent} commands.");
                return false;
            }

            var survivable = MoveGuard.SurvivableMoves(board);
            var choice = Preference.Where(survivable.Contains).Select(move => (ReactorCommand?)move).FirstOrDefault();
            if (choice is null)
            {
                Console.WriteLine($"  step {step}: boxed in at column {board.Robot?.Col}, nothing survivable.");
                return false;
            }

            if (verbose)
                Console.WriteLine($"  step {step}: column {board.Robot?.Col} -> {choice.Value.Name()}");

            reply = await api.SendAsync(choice.Value);
            board = reply.Board;
        }

        Console.WriteLine($"  gave up after {maxCommands} commands.");
        return false;
    }

    private static readonly ReactorCommand[] Preference = [ReactorCommand.Right, ReactorCommand.Wait, ReactorCommand.Left];
}
