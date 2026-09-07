using System.Text;
using System.Text.RegularExpressions;

namespace _02_04_zadanie.Mission;

public enum MissionFact
{
    Date,
    Password,
    ConfirmationCode
}

/// <summary>What happened when a researcher's finding was written to the blackboard.</summary>
public enum FindingOutcome
{
    /// <summary>First value for this fact.</summary>
    Accepted,

    /// <summary>A second researcher independently reported the same value.</summary>
    Confirmed,

    /// <summary>Two researchers disagree. Both are kept; the coordinator decides.</summary>
    Conflict,

    /// <summary>The value is already known to be wrong, so it was not made current.</summary>
    PreviouslyRejected
}

public sealed record Finding(MissionFact Fact, string Value, string Evidence, string MessageId, string ReportedBy, string? Notes)
{
    public DateTimeOffset At { get; } = DateTimeOffset.Now;

    public string Describe() =>
        $"{Value}  (from message {MessageId}, reported by {ReportedBy}, evidence: \"{Evidence}\")";
}

public sealed record SubmissionRecord(int Number, string Date, string Password, string ConfirmationCode, string Response)
{
    public DateTimeOffset At { get; } = DateTimeOffset.Now;
}

/// <summary>
/// The blackboard: the one piece of state every agent in the run shares. Researchers write
/// findings into it, the coordinator reads them and submits, and the hub's feedback lands back
/// here. Findings are kept as history rather than overwritten, so a disagreement between two
/// researchers is visible instead of silently resolved by whoever wrote last.
/// The flag is detected by code, never taken from the model's own words.
/// </summary>
public sealed partial class MissionState
{
    [GeneratedRegex(@"\{\{?FLG:[^}]+\}\}?")]
    private static partial Regex FlagRegex();

    private readonly object _writeLock = new();
    private readonly Dictionary<MissionFact, List<Finding>> _history = [];
    private readonly Dictionary<MissionFact, HashSet<string>> _rejected = [];
    private readonly List<SubmissionRecord> _submissions = [];
    private readonly List<string> _conflicts = [];

    public string? Flag { get; private set; }
    public bool DraftAccepted { get; private set; }

    public bool FlagReceived => Flag is not null;
    public bool IsComplete => FlagReceived || DraftAccepted;
    public int SubmissionCount => _submissions.Count;
    public string? LastFeedback => _submissions.Count > 0 ? _submissions[^1].Response : null;

    public bool HasAllFacts => Enum.GetValues<MissionFact>().All(fact => Current(fact) is not null);

    public IReadOnlyList<MissionFact> MissingFacts =>
        Enum.GetValues<MissionFact>().Where(fact => Current(fact) is null).ToList();

    /// <summary>The value in play for a fact: the newest finding that has not been rejected.</summary>
    public Finding? Current(MissionFact fact)
    {
        lock (_writeLock)
        {
            if (!_history.TryGetValue(fact, out var findings))
                return null;

            return findings.AsEnumerable().Reverse().FirstOrDefault(f => !IsRejectedUnsafe(fact, f.Value));
        }
    }

    public FindingOutcome RecordFinding(Finding finding)
    {
        lock (_writeLock)
        {
            var findings = _history.TryGetValue(finding.Fact, out var existing) ? existing : _history[finding.Fact] = [];
            var previous = findings.AsEnumerable().Reverse().FirstOrDefault(f => !IsRejectedUnsafe(finding.Fact, f.Value));
            findings.Add(finding);

            if (IsRejectedUnsafe(finding.Fact, finding.Value))
                return FindingOutcome.PreviouslyRejected;

            if (previous is null)
                return FindingOutcome.Accepted;

            if (string.Equals(previous.Value, finding.Value, StringComparison.Ordinal))
                return FindingOutcome.Confirmed;

            _conflicts.Add($"{finding.Fact}: {previous.ReportedBy} reported \"{previous.Value}\", {finding.ReportedBy} reported \"{finding.Value}\"");
            return FindingOutcome.Conflict;
        }
    }

    public void RejectValue(MissionFact fact, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        lock (_writeLock)
        {
            var rejected = _rejected.TryGetValue(fact, out var existing) ? existing : _rejected[fact] = new HashSet<string>(StringComparer.Ordinal);
            rejected.Add(value.Trim());
        }
    }

    public IReadOnlyCollection<string> RejectedValues(MissionFact fact)
    {
        lock (_writeLock)
            return _rejected.TryGetValue(fact, out var values) ? values.ToList() : [];
    }

    public bool IsRejected(MissionFact fact, string value)
    {
        lock (_writeLock)
            return IsRejectedUnsafe(fact, value);
    }

    private bool IsRejectedUnsafe(MissionFact fact, string value) =>
        _rejected.TryGetValue(fact, out var values) && values.Contains(value.Trim());

    public SubmissionRecord RecordSubmission(string date, string password, string confirmationCode, string response)
    {
        lock (_writeLock)
        {
            var record = new SubmissionRecord(_submissions.Count + 1, date, password, confirmationCode, response);
            _submissions.Add(record);
            return record;
        }
    }

    public void MarkDraftAccepted() => DraftAccepted = true;

    public void ScanForFlag(string text)
    {
        var match = FlagRegex().Match(text);
        if (match.Success)
            Flag ??= match.Value;
    }

    public string RenderStatus()
    {
        var sb = new StringBuilder();
        sb.AppendLine("MISSION STATUS");
        sb.AppendLine();

        foreach (var fact in Enum.GetValues<MissionFact>())
        {
            var current = Current(fact);
            sb.AppendLine($"{FactNames.ToApiName(fact)}: {(current is null ? "MISSING" : current.Describe())}");

            if (current?.Notes is { Length: > 0 } notes)
                sb.AppendLine($"  researcher notes: {notes}");

            var rejected = RejectedValues(fact);
            if (rejected.Count > 0)
                sb.AppendLine($"  known wrong: {string.Join(", ", rejected.Select(v => $"\"{v}\""))}");
        }

        lock (_writeLock)
        {
            if (_conflicts.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("CONFLICTING REPORTS (resolve before submitting):");
                foreach (var conflict in _conflicts)
                    sb.AppendLine($"  - {conflict}");
            }

            if (_submissions.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("SUBMISSIONS SO FAR:");
                foreach (var submission in _submissions)
                {
                    sb.AppendLine($"  #{submission.Number} date={submission.Date} password={submission.Password} confirmation_code={submission.ConfirmationCode}");
                    sb.AppendLine($"     result: {submission.Response}");
                }
            }
        }

        return sb.ToString().TrimEnd();
    }
}

public static class FactNames
{
    public static string ToApiName(MissionFact fact) => fact switch
    {
        MissionFact.Date => "date",
        MissionFact.Password => "password",
        MissionFact.ConfirmationCode => "confirmation_code",
        _ => throw new ArgumentOutOfRangeException(nameof(fact))
    };

    public static MissionFact? TryParse(string? name) => name?.Trim().ToLowerInvariant() switch
    {
        "date" => MissionFact.Date,
        "password" => MissionFact.Password,
        "confirmation_code" or "confirmationcode" or "code" => MissionFact.ConfirmationCode,
        _ => null
    };
}
