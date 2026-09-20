using _04_01_zadanie.Llm;
using _04_01_zadanie.Mission;

namespace _04_01_zadanie.Agents;

/// <summary>
/// The run's grip on the loop: it scans every result for the flag, tells the agent after each call
/// where the checklist stands, and refuses an ending that has not made the three changes. The
/// budget and the checklist ride along on every tool result because the agent's own account of its
/// progress is not what the run ends on — the projection of the console is.
/// </summary>
public sealed class OkoHooks(Operation operation) : IAgentHooks
{
    public Task<string?> BeforeToolCallAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        if (operation.IsSettled)
            return Task.FromResult<string?>("The errand is already settled; nothing more needs to be sent.");

        return Task.FromResult<string?>(null);
    }

    public Task<string> AfterToolResultAsync(ToolCall call, string result, CancellationToken cancellationToken = default)
    {
        operation.Mission.ScanForFlag(result);
        return Task.FromResult($"{result}{Environment.NewLine}{Environment.NewLine}{Status()}");
    }

    public Task<string?> BeforeFinishAsync(CancellationToken cancellationToken = default)
    {
        if (operation.IsSettled)
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(
            $"The errand is not finished. The changes still outstanding:{Environment.NewLine}{operation.Mission.RenderChecklist()}" +
            $"{Environment.NewLine}Make the remaining changes with update_record, then run finish_mission.");
    }

    private string Status() =>
        $"[checklist]{Environment.NewLine}{operation.Mission.RenderChecklist()}{Environment.NewLine}{operation.RenderBudget()}";
}
