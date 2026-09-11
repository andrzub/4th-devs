using System.Text.Json;
using _03_02_zadanie.Shell;

namespace _03_02_zadanie.Tools;

/// <summary>
/// Rebuilds the machine from disk. It sits in its own tool rather than among the ordinary commands
/// because it throws away every edit made so far: asking for it has to be a decision, not a stray
/// command in a list, and the number of times it can happen is capped.
/// </summary>
public sealed class RebootTool(ShellSession session, int maxReboots) : ITool
{
    private int _rebootCount;

    public string Name => "reboot_machine";

    public string Description =>
        "Rebuild the virtual machine's filesystem from disk, undoing every change made during this " +
        "run. Use it only when the machine is in a state you cannot recover from by editing files.";

    public JsonElement ParametersSchema => JsonDocument.Parse(
        """
        {
          "type": "object",
          "properties": {
            "reason": {
              "type": "string",
              "description": "What is broken badly enough to be worth losing every change made so far."
            }
          },
          "required": ["reason"],
          "additionalProperties": false
        }
        """).RootElement;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var reason = ToolArguments.Parse(argumentsJson).GetString("reason");
        if (string.IsNullOrWhiteSpace(reason))
            return "State what is broken in the 'reason' argument before rebuilding the machine.";

        if (_rebootCount >= maxReboots)
            return $"The machine has already been rebuilt {_rebootCount} time(s), which is the limit for this run. Work with the state you have.";

        _rebootCount++;
        Console.WriteLine($"[shell] rebooting ({_rebootCount}/{maxReboots}): {reason}");

        var response = await session.RebootAsync(cancellationToken);
        return $"{response.Render(2000)}{Environment.NewLine}Every earlier change is gone. {session.Status()}";
    }
}
