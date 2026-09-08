using System.Text;
using System.Text.Json;
using _02_05_zadanie.Hub;
using _02_05_zadanie.Mission;

namespace _02_05_zadanie.Tools;

/// <summary>
/// The agent's only action: hand the drone a complete instruction list and read what the
/// on-board system says back. Everything else it needs — the manual and the sector of the dam —
/// is already in its prompt, because both are known before the loop starts and neither changes.
/// A list that fails the local checks never reaches the hub, so a malformed argument costs
/// an iteration instead of one of the few submissions the run is allowed.
/// </summary>
public sealed class SendInstructionsTool(HubClient hub, InstructionValidator validator, MissionState state) : ITool
{
    public string Name => "send_instructions";

    public string Description =>
        "Sends one complete instruction list to the drone and returns its response verbatim. " +
        "Each call is one real submission out of a small budget, and the drone keeps its configuration " +
        "between calls, so send the whole mission as a single list rather than building it up across calls.";

    public JsonElement ParametersSchema => JsonDocument.Parse(
        """
        {
          "type": "object",
          "properties": {
            "instructions": {
              "type": "array",
              "items": { "type": "string" },
              "description": "The instruction list in execution order, each entry exactly as the manual writes it, e.g. \"set(engineON)\" or \"flyToLocation\"."
            }
          },
          "required": ["instructions"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var instructions = ToolArguments.Parse(argumentsJson).GetStringArray("instructions") ?? [];

        var validation = validator.Validate(instructions);
        if (!validation.IsValid)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Not sent — the list was rejected before it reached the drone:");
            foreach (var error in validation.Errors)
                sb.AppendLine($"  - {error}");
            sb.Append("Fix the list and call this tool again. This did not cost a submission.");
            return sb.ToString();
        }

        var submission = await hub.SendInstructionsAsync(instructions, cancellationToken);
        state.Record(instructions, submission.StatusCode, submission.Body);

        return $"{submission.Render()}{Environment.NewLine}(submission {submission.Number}, {hub.RemainingSubmissions} left)";
    }
}
