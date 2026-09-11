using System.Text.Json;
using _03_02_zadanie.Shell;

namespace _03_02_zadanie.Tools;

/// <summary>
/// The agent's only way to touch the machine. One command per call, because the API takes one
/// command per request and the machine keeps its state between them.
/// </summary>
public sealed class RunCommandTool(ShellSession session) : ITool
{
    public string Name => "run_command";

    public string Description =>
        "Run a single command on the controller's virtual machine and return its raw output. " +
        "The machine keeps its working directory and its files between calls. Commands that would " +
        "touch off-limits directories or blacklisted files are refused here, before they are sent.";

    public JsonElement ParametersSchema => JsonDocument.Parse(
        """
        {
          "type": "object",
          "properties": {
            "command": {
              "type": "string",
              "description": "One command line, exactly as the machine's own help documents it. To run a program, give its absolute path as the whole command."
            }
          },
          "required": ["command"],
          "additionalProperties": false
        }
        """).RootElement;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var command = ToolArguments.Parse(argumentsJson).GetString("command");

        return string.IsNullOrWhiteSpace(command)
            ? "No command given. Pass the command line in the 'command' argument."
            : await session.ExecuteAsync(command, cancellationToken);
    }
}
