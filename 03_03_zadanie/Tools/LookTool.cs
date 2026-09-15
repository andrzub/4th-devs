using System.Text.Json;
using _03_03_zadanie.Reactor;

namespace _03_03_zadanie.Tools;

/// <summary>
/// Reads the board. This is the cheap half of the loop: the preview endpoint reports the reactor
/// without advancing it, so looking as often as needed costs neither a tick nor a robot.
/// </summary>
public sealed class LookTool(ReactorSession session) : ITool
{
    public string Name => "look";

    public string Description =>
        "Reads the current state of the reactor: the board, every block with the direction of its next move, "
        + "where the robot stands and which commands survive this tick. Does not move the robot and does not "
        + "advance the blocks, so it is free to call.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""{"type":"object","properties":{},"required":[]}""").RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default) => session.LookAsync(cancellationToken);
}
