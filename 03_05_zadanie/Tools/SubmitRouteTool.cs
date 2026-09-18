using System.Text.Json;
using _03_05_zadanie.Mission;

namespace _03_05_zadanie.Tools;

/// <summary>
/// The only way out to the hub. Every route is replayed against the registered rules first, so a
/// route that runs out of food, drives into water or stops short of the goal is turned back here —
/// a refusal costs one turn, a rejection from the hub costs one of the few attempts the run has.
/// </summary>
public sealed class SubmitRouteTool(Expedition expedition) : ITool
{
    public string Name => "submit_route";

    public string Description =>
        "Sends one complete route to headquarters: the departure mode first, then the moves. The route is checked "
        + "against the registered rules before anything leaves this machine, and the reply comes back raw. Only a "
        + "route that reaches the goal within both budgets is sent.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {"type":"object","properties":{"instructions":{"type":"array","items":{"type":"string"},
          "description":"Departure mode first, then up, down, left, right and dismount."}},
         "required":["instructions"]}
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var instructions = ToolArguments.Parse(argumentsJson).GetStringArray("instructions");
        return instructions is null or { Count: 0 }
            ? Task.FromResult("The route is empty. Send the departure mode followed by the moves.")
            : expedition.SubmitAsync(instructions, cancellationToken);
    }
}
