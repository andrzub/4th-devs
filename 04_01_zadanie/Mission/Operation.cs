using System.Text;
using _04_01_zadanie.Hub;
using _04_01_zadanie.Oko;

namespace _04_01_zadanie.Mission;

/// <summary>
/// Ties the read-only console to the write-only API and the checklist that decides when the errand
/// is done. Reconnaissance is done here in code before the agent's first turn: every listing is
/// read, so the loop begins knowing which records exist and which identifiers address them, instead
/// of spending turns — and one of the run's few requests each — discovering the console has any.
/// </summary>
public sealed class Operation(OkoPanelClient panel, OkoEditorClient editor, MissionState mission, TaskSettings settings, bool submissionEnabled)
{
    public MissionState Mission => mission;

    public OkoEditorClient Editor => editor;

    public bool SubmissionEnabled => submissionEnabled;

    /// <summary>
    /// The run ends on a flag from the API. With submissions off there is no flag to wait for, so
    /// the deliverable is the checklist reading complete against the projection of the console.
    /// </summary>
    public bool IsSettled => submissionEnabled ? mission.FlagReceived : mission.AllObjectivesMet;

    public async Task<string> BootstrapAsync(CancellationToken cancellationToken = default)
    {
        await panel.SignInAsync(cancellationToken);

        var digest = new StringBuilder();
        digest.AppendLine("The operator console was read before your first turn. Everything below is reconnaissance only — nothing here changed the console.");
        digest.AppendLine();

        foreach (var page in OkoPages.All)
        {
            var records = await panel.ListAsync(page, cancellationToken);
            mission.Remember(records);

            digest.AppendLine($"=== {page} ({records.Count}) ===");
            if (records.Count == 0)
                digest.AppendLine("  (no records addressable by id on this page)");
            foreach (var record in records)
                digest.AppendLine(record.Describe());
            digest.AppendLine();
        }

        digest.AppendLine("Read the records that matter in full before rewriting them — a listing shows only the start of each description.");
        return digest.ToString().TrimEnd();
    }

    public string RenderBudget() =>
        $"[budget: {editor.RemainingRequests} okoeditor calls left of {settings.MaxHubRequests}; console reads so far: {panel.RequestsSent}]";
}
