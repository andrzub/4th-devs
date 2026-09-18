using System.Text;
using System.Text.RegularExpressions;

namespace _03_05_zadanie.Mission;

public sealed record SubmissionRecord(int Number, string Route, bool Sent, string Outcome);

/// <summary>
/// What the run knows about its own progress. The flag is picked out of the hub's raw answer by a
/// regex rather than taken from the model's retelling of it, so a run can only end on a flag that
/// really arrived.
/// </summary>
public sealed partial class MissionState
{
    [GeneratedRegex(@"\{\{?FLG:[^}]+\}\}?")]
    private static partial Regex FlagRegex();

    private readonly List<SubmissionRecord> _history = [];

    public string? Flag { get; private set; }

    public bool FlagReceived => Flag is not null;

    public IReadOnlyList<SubmissionRecord> History => _history;

    /// <summary>Routes the guard turned back, which cost a turn but never a submission.</summary>
    public int RefusedCount => _history.Count(record => !record.Sent);

    public void ScanForFlag(string text)
    {
        var match = FlagRegex().Match(text);
        if (match.Success)
            Flag ??= match.Value;
    }

    public SubmissionRecord Record(string route, bool sent, string outcome)
    {
        var record = new SubmissionRecord(_history.Count + 1, route, sent, outcome);
        _history.Add(record);
        return record;
    }

    public string RenderHistory()
    {
        if (_history.Count == 0)
            return "No route has been submitted yet.";

        var sb = new StringBuilder();
        foreach (var record in _history)
            sb.AppendLine($"#{record.Number,-3} {(record.Sent ? "sent    " : "refused ")} {record.Route}{Environment.NewLine}     {record.Outcome}");

        return sb.ToString().TrimEnd();
    }
}
