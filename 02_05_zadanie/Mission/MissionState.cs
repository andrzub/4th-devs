using System.Text;
using System.Text.RegularExpressions;

namespace _02_05_zadanie.Mission;

public sealed record AttemptRecord(int Number, IReadOnlyList<string> Instructions, int StatusCode, string Response)
{
    public DateTimeOffset At { get; } = DateTimeOffset.Now;
}

/// <summary>
/// What the run knows about its own progress. The flag is detected by code from the hub's
/// own words: the model never gets to declare the mission finished, and a run that ends
/// without a real flag in a real response ends unfinished.
/// </summary>
public sealed partial class MissionState
{
    [GeneratedRegex(@"\{\{?FLG:[^}]+\}\}?")]
    private static partial Regex FlagRegex();

    private readonly List<AttemptRecord> _attempts = [];

    public string? Flag { get; private set; }
    public bool FlagReceived => Flag is not null;
    public int AttemptCount => _attempts.Count;

    public AttemptRecord Record(IReadOnlyList<string> instructions, int statusCode, string response)
    {
        var attempt = new AttemptRecord(_attempts.Count + 1, instructions, statusCode, response);
        _attempts.Add(attempt);
        ScanForFlag(response);
        return attempt;
    }

    public void ScanForFlag(string text)
    {
        var match = FlagRegex().Match(text);
        if (match.Success)
            Flag ??= match.Value;
    }

    public string RenderHistory()
    {
        if (_attempts.Count == 0)
            return "No instruction set has been sent yet.";

        var sb = new StringBuilder();
        foreach (var attempt in _attempts)
        {
            sb.AppendLine($"#{attempt.Number}  [{string.Join(", ", attempt.Instructions)}]");
            sb.AppendLine($"     HTTP {attempt.StatusCode}: {attempt.Response}");
        }

        return sb.ToString().TrimEnd();
    }
}
