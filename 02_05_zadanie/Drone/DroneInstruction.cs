using System.Text.RegularExpressions;

namespace _02_05_zadanie.Drone;

/// <summary>
/// One parsed API call from an instruction list. The drone's API overloads a single name
/// (<c>set</c>) across six unrelated functions and tells them apart by the shape of the
/// argument, so the argument is kept verbatim and interpreted only where a rule needs it.
/// </summary>
public sealed partial record DroneInstruction(string Raw, string Name, string? Arguments)
{
    public bool HasArguments => Arguments is not null;

    /// <summary>The landing sector, when this instruction is the two-number <c>set(x,y)</c> overload.</summary>
    public (int Column, int Row)? AsLandingSector()
    {
        if (!string.Equals(Name, "set", StringComparison.OrdinalIgnoreCase) || Arguments is null)
            return null;

        var parts = Arguments.Split(',');
        return parts.Length == 2
            && int.TryParse(parts[0].Trim(), out var column)
            && int.TryParse(parts[1].Trim(), out var row)
                ? (column, row)
                : null;
    }

    public static DroneInstruction Parse(string raw)
    {
        var trimmed = raw.Trim();
        var match = CallRegex().Match(trimmed);
        return match.Success
            ? new DroneInstruction(trimmed, match.Groups["name"].Value, match.Groups["args"].Value.Trim())
            : new DroneInstruction(trimmed, trimmed, null);
    }

    public static string LandingSector(int column, int row) => $"set({column},{row})";
    public static string DestinationObject(string objectId) => $"setDestinationObject({objectId})";
    public const string StartFlight = "flyToLocation";

    [GeneratedRegex(@"^(?<name>[A-Za-z][A-Za-z0-9_]*)\s*\(\s*(?<args>.*?)\s*\)$", RegexOptions.Singleline)]
    private static partial Regex CallRegex();
}
