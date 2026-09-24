using System.Text.Json;
using _04_05_zadanie.Mission;

namespace _04_05_zadanie.Tools;

/// <summary>
/// Runs the validator over the whole plan. Free and local. A clean report ends the run; a report
/// with warnings only ends it when the agent says it has read them and keeps the plan anyway.
/// </summary>
public sealed class CheckPlanTool(MissionState state) : ITool
{
    public const string ToolName = "check_plan";

    public string Name => ToolName;

    public string Description =>
        "Checks the whole plan against the demand list and against what was read from the database: one order per city, every destination and creator observed, " +
        "logins and birthdays matching their rows, creators active. Errors must be fixed. Warnings point at something worth a second look; " +
        "pass accept_warnings=true to keep the plan despite them. The work is done when the plan is accepted.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "accept_warnings": { "type": "boolean", "description": "true to accept a plan that is valid but has warnings you have considered." }
          },
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var acceptWarnings = ToolArguments.Parse(argumentsJson).GetBool("accept_warnings") ?? false;
        var report = state.Check();
        var accepted = state.TryAccept(acceptWarnings);

        var status = accepted
            ? "The plan is accepted; the orders will be placed by code from this plan."
            : report.IsValid
                ? "The plan is valid but has warnings. Change the plan, or call check_plan with accept_warnings=true to keep it as it is."
                : "Fix the errors with register_orders (registering a city again replaces its entry), then check again.";

        return Task.FromResult($"{state.Plan.Render()}{Environment.NewLine}{Environment.NewLine}{report.Render()}{Environment.NewLine}{Environment.NewLine}{status}");
    }
}
