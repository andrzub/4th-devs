using _04_05_zadanie.Hub;
using _04_05_zadanie.Llm;
using _04_05_zadanie.Mission;
using _04_05_zadanie.Tools;

namespace _04_05_zadanie.Agents;

/// <summary>
/// The run's grip on the loop. "Look at the existing orders before you plan" is enforced rather
/// than hoped for: a registration is refused until the orders have been listed. After each call the
/// agent sees what has been observed and what is still missing, counted by code, and an attempt to
/// finish is turned back with the validator's findings until the plan is valid.
/// </summary>
public sealed class WarehouseHooks(MissionState state, FoodwarehouseClient hub) : IAgentHooks
{
    public Task<string?> BeforeToolCallAsync(ToolCall call, CancellationToken cancellationToken = default)
    {
        if (call.FunctionName == RegisterOrdersTool.ToolName && !state.Observations.OrdersListed)
            return Task.FromResult<string?>("Refused: list the existing orders first (list_orders). They are the only example of an order the warehouse has accepted; study their creators before choosing yours.");

        return Task.FromResult<string?>(null);
    }

    public Task<string> AfterToolResultAsync(ToolCall call, string result, CancellationToken cancellationToken = default) =>
        Task.FromResult($"{result}{Environment.NewLine}{state.RenderProgress()} [hub requests left: {hub.RemainingRequests}]");

    public Task<string?> BeforeFinishAsync(CancellationToken cancellationToken = default)
    {
        var report = state.Check();
        if (report.IsValid)
            return Task.FromResult<string?>(null);

        return Task.FromResult<string?>(
            $"The plan is not complete yet. The validator found:{Environment.NewLine}{report.Render()}{Environment.NewLine}" +
            "Read what is missing from the database, register it with register_orders, then run check_plan.");
    }
}
