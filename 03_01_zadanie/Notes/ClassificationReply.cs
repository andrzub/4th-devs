using System.Text.RegularExpressions;

namespace _03_01_zadanie.Notes;

/// <summary>
/// The model's answer to one batch, reduced to the item numbers it singled out. The reply
/// format lists only the exceptions, because almost every statement is routine and output
/// tokens cost several times what input does — but that also means a truncated or lazy reply
/// looks exactly like "nothing to report", so parsing is strict and anything unexpected is
/// reported as an error rather than shrugged off.
/// </summary>
public sealed class ClassificationReply
{
    private ClassificationReply(HashSet<int> problem, HashSet<int> unclear, string? error)
    {
        Problem = problem;
        Unclear = unclear;
        Error = error;
    }

    public HashSet<int> Problem { get; }
    public HashSet<int> Unclear { get; }

    /// <summary>Why the reply is unusable, or null when it parsed cleanly.</summary>
    public string? Error { get; }

    public NoteTone ToneOf(int itemNumber) =>
        Problem.Contains(itemNumber) ? NoteTone.Problem
        : Unclear.Contains(itemNumber) ? NoteTone.Unclear
        : NoteTone.Ok;

    public static ClassificationReply Parse(string? content, int itemCount)
    {
        var problem = new HashSet<int>();
        var unclear = new HashSet<int>();

        if (string.IsNullOrWhiteSpace(content))
            return new ClassificationReply(problem, unclear, "empty reply");

        var problemLine = Regex.Match(content, @"^\s*PROBLEM\s*:(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        var unclearLine = Regex.Match(content, @"^\s*UNCLEAR\s*:(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);

        if (!problemLine.Success || !unclearLine.Success)
            return new ClassificationReply(problem, unclear, "reply is missing the PROBLEM or UNCLEAR line");

        foreach (var (line, target) in new[] { (problemLine, problem), (unclearLine, unclear) })
        {
            foreach (var token in line.Groups[1].Value.Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (token == "-" || token.Equals("none", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!int.TryParse(token, out var number) || number < 1 || number > itemCount)
                    return new ClassificationReply(problem, unclear, $"reply names item '{token}', which is not in 1..{itemCount}");

                target.Add(number);
            }
        }

        var overlap = problem.Intersect(unclear).ToList();
        return overlap.Count > 0
            ? new ClassificationReply(problem, unclear, $"items {string.Join(',', overlap)} listed as both PROBLEM and UNCLEAR")
            : new ClassificationReply(problem, unclear, null);
    }
}
