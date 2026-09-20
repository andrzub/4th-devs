using System.Text.Json;
using _04_01_zadanie.Hub;
using _04_01_zadanie.Mission;

namespace _04_01_zadanie.Tools;

/// <summary>
/// Runs the API's own verification, but only once the checklist says the three changes are in
/// place. The gate is not absolute: the centre's list and this checklist are two readings of the
/// same errand, and if they disagree the API's verdict is the one that counts — so after a few
/// refusals the call goes through with the disagreement stated, rather than trapping a finished
/// run behind a check of our own making.
/// </summary>
public sealed class FinishMissionTool(OkoEditorClient client, MissionState state, bool dryRun, int maxRefusals = 2) : ITool
{
    public string Name => "finish_mission";

    public string Description =>
        "Runs the 'done' action, which checks every required change at once and returns the flag only when all of them are in place. " +
        "Takes no arguments: it reports on the console as it now stands.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        { "type": "object", "properties": {}, "additionalProperties": false }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        if (!state.AllObjectivesMet && state.NoteFinishRefusal() <= maxRefusals)
            return $"Not sent — the changes are not all in place yet, and this call would spend a request to be told so:{Environment.NewLine}{state.RenderChecklist()}";

        if (dryRun)
            return $"DRY RUN — 'done' was not sent. The checklist as it stands:{Environment.NewLine}{state.RenderChecklist()}";

        var reply = await client.DoneAsync(cancellationToken);
        state.ScanForFlag(reply.Body);

        var disagreement = state.AllObjectivesMet
            ? string.Empty
            : $"{Environment.NewLine}(Sent although the local checklist still shows work outstanding:{Environment.NewLine}{state.RenderChecklist()})";

        return $"HTTP {reply.Status}{Environment.NewLine}{reply.Body}{disagreement}";
    }
}
