using System.Text.RegularExpressions;

namespace _02_03_zadanie.Mission;

/// <summary>
/// Shared run state. The flag is detected by code, not by the model: the agent loop refuses
/// to finish until a real flag has been observed in a submission result (or, in draft mode,
/// until one digest has passed the local checks and been handed to the submit tool).
/// The last digest that passed check_digest is kept here so submit_logs can send it without
/// the model repeating the whole text in its context.
/// </summary>
public partial class MissionState
{
    [GeneratedRegex(@"\{\{?FLG:[^}]+\}\}?")]
    private static partial Regex FlagRegex();

    public string? Flag { get; private set; }
    public int SubmissionCount { get; private set; }
    public bool DraftAccepted { get; private set; }
    public string? LastCheckedDigest { get; private set; }

    public bool FlagReceived => Flag is not null;
    public bool IsComplete => FlagReceived || DraftAccepted;

    public int NextSubmissionNumber() => ++SubmissionCount;

    public void MarkDraftAccepted() => DraftAccepted = true;

    public void RememberCheckedDigest(string digest) => LastCheckedDigest = digest;

    public void ScanForFlag(string text)
    {
        var match = FlagRegex().Match(text);
        if (match.Success)
            Flag ??= match.Value;
    }
}
