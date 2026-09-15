using System.Text;
using System.Text.RegularExpressions;

namespace _03_03_zadanie.Mission;

public sealed record CommandRecord(int Number, string Command, bool Sent, string Outcome);

/// <summary>
/// What the run knows about its own progress. The flag is picked out of the reactor's raw answer by
/// a regex rather than taken from the model's summary of it, so a run can only end on a flag that
/// really arrived.
/// </summary>
public sealed partial class MissionState
{
    [GeneratedRegex(@"\{\{?FLG:[^}]+\}\}?")]
    private static partial Regex FlagRegex();

    private readonly List<CommandRecord> _history = [];

    public string? Flag { get; private set; }

    public bool FlagReceived => Flag is not null;

    public IReadOnlyList<CommandRecord> History => _history;

    /// <summary>Commands the guard turned back, which cost a turn but never a request.</summary>
    public int RefusedCount => _history.Count(record => !record.Sent);

    public void ScanForFlag(string text)
    {
        var match = FlagRegex().Match(text);
        if (match.Success)
            Flag ??= match.Value;
    }

    public CommandRecord Record(string command, bool sent, string outcome)
    {
        var record = new CommandRecord(_history.Count + 1, command, sent, outcome);
        _history.Add(record);
        return record;
    }

    public string RenderHistory()
    {
        if (_history.Count == 0)
            return "Nothing has been sent to the reactor yet.";

        var sb = new StringBuilder();
        foreach (var record in _history)
            sb.AppendLine($"#{record.Number,-3} {(record.Sent ? "sent    " : "refused ")} {record.Command,-6} {record.Outcome}");

        return sb.ToString().TrimEnd();
    }
}
