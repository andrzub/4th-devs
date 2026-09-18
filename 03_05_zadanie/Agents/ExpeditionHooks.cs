using _03_05_zadanie.Llm;
using _03_05_zadanie.Mission;

namespace _03_05_zadanie.Agents;

/// <summary>
/// The run's grip on the loop: it stops calls that cannot help, tells the agent after every call
/// where the mission actually stands, and refuses an ending that has not delivered a route.
/// </summary>
public sealed class ExpeditionHooks(Expedition expedition) : IAgentHooks
{
    public Task<string?> BeforeToolCallAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        if (expedition.IsSettled)
            return Task.FromResult<string?>("The mission is already settled; nothing more needs to be sent.");

        if (call.FunctionName == "ask_tool" && expedition.Registry.Known.Count == 0)
            return Task.FromResult<string?>("No tool has been discovered yet — search the registry first.");

        return Task.FromResult<string?>(null);
    }

    public Task<string> AfterToolResultAsync(ToolCall call, string result, CancellationToken cancellationToken = default)
    {
        expedition.Mission.ScanForFlag(result);
        return Task.FromResult($"{result}{Environment.NewLine}{Environment.NewLine}{Status()}");
    }

    public Task<string?> BeforeFinishAsync(CancellationToken cancellationToken = default)
    {
        if (expedition.IsSettled)
            return Task.FromResult<string?>(null);

        var demand = expedition.World is null
            ? "The rules have not been registered yet, so no route can be planned. Keep looking and register what you find."
            : expedition.LastPlan?.Best is null
                ? "No route has been planned yet that survives both budgets. Plan again, or go back and find the rule that is still missing."
                : "A route was planned but never accepted. Submit it, and if it comes back refused, use what the refusal says.";

        return Task.FromResult<string?>(demand);
    }

    private string Status()
    {
        var tools = expedition.Registry.Known.Count == 0
            ? "none"
            : string.Join(", ", expedition.Registry.Known.Select(tool => tool.Name));

        var world = expedition.World is null ? "not registered" : "registered";
        var plan = expedition.LastPlan?.Best is null ? "none" : expedition.LastPlan.Best.Summary;

        return $"[mission] tools found: {tools} | world: {world} | best planned route: {plan} | refused routes: {expedition.Mission.RefusedCount}";
    }
}
