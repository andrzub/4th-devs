using System.Text.Json;
using _04_04_zadanie.Mission;

namespace _04_04_zadanie.Tools;

/// <summary>
/// Runs the validator over the whole projection. Free and local, so the agent can call it as often
/// as it likes; a clean report is what ends the run.
/// </summary>
public sealed class CheckPlanTool(FilingState state) : ITool
{
    public string Name => "check_plan";

    public string Description =>
        "Checks the whole filesystem being built against the notes: every city has one person, every good sold in the ledger " +
        "has a file linking each seller, every quantity is on the announcements board. The work is done when it reports the plan valid.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var report = state.Check();
        return Task.FromResult($"{state.Filesystem.Render()}{Environment.NewLine}{Environment.NewLine}{report.Render()}");
    }
}
