using System.Text.Json;
using _03_05_zadanie.Mission;

namespace _03_05_zadanie.Tools;

/// <summary>
/// The arithmetic half of the task. Two resources drain at different rates per mode, so the cheapest
/// route is a comparison over every departure mode rather than a single shortest path — and that is
/// exactly the kind of counting a model should not be doing in its head.
/// </summary>
public sealed class PlanRouteTool(Expedition expedition) : ITool
{
    public string Name => "plan_route";

    public string Description =>
        "Computes the cheapest route to the goal for every departure mode from the rules registered so far, "
        + "including where to leave the vehicle, and reports which modes cannot make it and why. Free to call: it "
        + "touches no external service. Call it again after registering anything new.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""{"type":"object","properties":{},"required":[]}""").RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default) =>
        Task.FromResult(expedition.Plan());
}
