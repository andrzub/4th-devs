using System.Text.RegularExpressions;

namespace _02_02_zadanie.Mission;

/// <summary>
/// Shared run state. The flag is detected by code, not by the model — the agent loop
/// refuses to finish until a real flag has been observed in a tool result.
/// </summary>
public partial class MissionState
{
    [GeneratedRegex(@"\{\{?FLG:[^}]+\}\}?")]
    private static partial Regex FlagRegex();

    public string? Flag { get; private set; }
    public int RotationCount { get; private set; }

    public bool FlagReceived => Flag is not null;

    public void RecordRotation() => RotationCount++;

    public void ScanForFlag(string text)
    {
        var match = FlagRegex().Match(text);
        if (match.Success)
            Flag ??= match.Value;
    }
}
