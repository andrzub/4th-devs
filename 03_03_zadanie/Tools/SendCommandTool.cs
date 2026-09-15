using System.Text.Json;
using _03_03_zadanie.Reactor;

namespace _03_03_zadanie.Tools;

/// <summary>
/// Sends one command to the robot. Every call is one tick of the reactor: the blocks move whether
/// the robot does or not, which is why 'wait' is a move and not a pause.
/// </summary>
public sealed class SendCommandTool(ReactorSession session) : ITool
{
    public string Name => "send_command";

    public string Description =>
        "Sends exactly one command to the robot and advances the reactor by one tick. "
        + "'start' opens the run, 'left' and 'right' move the robot along the bottom row, 'wait' holds position "
        + "while the blocks move, and 'reset' returns the robot to the start. A command that would put the robot "
        + "under a block is refused before it is sent.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {"type":"object","properties":{"command":{"type":"string","enum":["start","reset","left","wait","right"],"description":"The single command to send."}},"required":["command"]}
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var requested = ToolArguments.Parse(argumentsJson).GetString("command");
        return ReactorCommands.TryParse(requested, out var command)
            ? session.SendAsync(command, cancellationToken)
            : Task.FromResult($"'{requested}' is not a command the reactor knows. Valid commands: {string.Join(", ", ReactorCommands.All)}.");
    }
}
