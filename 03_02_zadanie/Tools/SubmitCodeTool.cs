using System.Text.Json;
using _03_02_zadanie.Hub;
using _03_02_zadanie.Mission;

namespace _03_02_zadanie.Tools;

/// <summary>
/// Sends the confirmation code to the hub. The tool takes no code argument on purpose: it submits
/// what the machine actually printed, as captured from its raw output, so a code cannot drift by
/// a character on its way through the model's summary of it.
/// </summary>
public sealed class SubmitCodeTool(HubClient hub, MissionState mission, bool submissionEnabled) : ITool
{
    public string Name => "submit_code";

    public string Description =>
        "Send the confirmation code printed by the cooling controller to headquarters. Takes no " +
        "arguments: the code captured from the machine's own output is the one that gets sent. " +
        "Call this only once the code has appeared in a command's output.";

    public JsonElement ParametersSchema => JsonDocument.Parse(
        """
        {
          "type": "object",
          "properties": {},
          "additionalProperties": false
        }
        """).RootElement;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        if (mission.ConfirmationCode is not { } code)
            return DescribeMissingCode();

        if (!submissionEnabled)
            return $"Submission is switched off for this run, so nothing was sent. The code {code} has been captured and the work is done; it will be submitted separately.";

        var submission = await hub.SubmitConfirmationAsync(code, cancellationToken);
        mission.Record(code, submission.StatusCode, submission.Body);

        return $"{submission.Render()}{Environment.NewLine}{hub.RemainingSubmissions} submission(s) left.";
    }

    private string DescribeMissingCode()
    {
        var baseMessage = "No confirmation code has appeared in any command output yet, so there is nothing to submit. Get the controller to print it first.";

        return mission.MalformedCandidates.Count == 0
            ? baseMessage
            : $"{baseMessage} Something starting with ECCS- did show up but not in the documented shape (ECCS- followed by 40 letters and digits): {string.Join(", ", mission.MalformedCandidates)}.";
    }
}
