using System.Text;
using System.Text.RegularExpressions;

namespace _03_02_zadanie.Mission;

public sealed record SubmissionRecord(int Number, string Code, int StatusCode, string Body)
{
    public bool IsSuccess => StatusCode is >= 200 and < 300;
}

/// <summary>
/// What the run knows about its own progress. The confirmation code is picked out of the machine's
/// raw output by code rather than copied from the model's summary of it: forty characters retyped
/// by a language model is exactly the kind of detail that quietly loses a character.
/// </summary>
public sealed partial class MissionState
{
    [GeneratedRegex(@"ECCS-[A-Za-z0-9]{40}")]
    private static partial Regex CodeRegex();

    /// <summary>Catches a code that does not have the documented shape, so it can be reported rather than silently missed.</summary>
    [GeneratedRegex(@"ECCS-\S+")]
    private static partial Regex CodeCandidateRegex();

    [GeneratedRegex(@"\{\{?FLG:[^}]+\}\}?")]
    private static partial Regex FlagRegex();

    private readonly List<SubmissionRecord> _submissions = [];
    private readonly List<string> _malformedCandidates = [];

    public string? ConfirmationCode { get; private set; }
    public string? Flag { get; private set; }
    public bool FlagReceived => Flag is not null;
    public bool CodeFound => ConfirmationCode is not null;
    public IReadOnlyList<string> MalformedCandidates => _malformedCandidates;
    public IReadOnlyList<SubmissionRecord> Submissions => _submissions;

    public void ScanForCode(string text)
    {
        var match = CodeRegex().Match(text);
        if (match.Success)
        {
            ConfirmationCode ??= match.Value;
            return;
        }

        foreach (var candidate in CodeCandidateRegex().Matches(text).Select(m => m.Value).Distinct())
        {
            if (!_malformedCandidates.Contains(candidate))
                _malformedCandidates.Add(candidate);
        }
    }

    public void ScanForFlag(string text)
    {
        var match = FlagRegex().Match(text);
        if (match.Success)
            Flag ??= match.Value;
    }

    public SubmissionRecord Record(string code, int statusCode, string body)
    {
        var submission = new SubmissionRecord(_submissions.Count + 1, code, statusCode, body);
        _submissions.Add(submission);
        ScanForFlag(body);
        return submission;
    }

    public string RenderHistory()
    {
        if (_submissions.Count == 0)
            return "Nothing has been submitted to the hub yet.";

        var sb = new StringBuilder();
        foreach (var submission in _submissions)
            sb.AppendLine($"#{submission.Number}  {submission.Code}  ->  HTTP {submission.StatusCode}: {submission.Body}");

        return sb.ToString().TrimEnd();
    }
}
