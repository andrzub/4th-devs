using System.Text.RegularExpressions;
using _02_05_zadanie.Drone;

namespace _02_05_zadanie.Mission;

public sealed record InstructionValidation(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// The last thing standing between the model and a dropped charge. Two kinds of check live here.
///
/// Format checks are the cheap ones: the manual states the accepted shape of every argument, so
/// a malformed one is caught locally instead of spending a submission to be told the same thing.
///
/// The mission check is the one that matters. The whole point of the operation is that the flight
/// is registered against the power plant while the charge lands on the dam, and the drone carries
/// a single charge — so a wrong sector cannot be corrected on the next attempt. The sector is
/// therefore not something the model is asked to remember correctly; a list that starts a flight
/// without carrying exactly the sector found on the map does not leave this process.
/// </summary>
public sealed partial class InstructionValidator(int damColumn, int damRow, string targetObjectId, int gridColumns, int gridRows)
{
    public string RequiredLandingSector => DroneInstruction.LandingSector(damColumn, damRow);
    public string RequiredDestinationObject => DroneInstruction.DestinationObject(targetObjectId);

    public InstructionValidation Validate(IReadOnlyList<string> rawInstructions)
    {
        var errors = new List<string>();

        if (rawInstructions.Count == 0)
            return new InstructionValidation(["The instruction list is empty; the hub requires at least one instruction."]);

        if (rawInstructions.Any(string.IsNullOrWhiteSpace))
            errors.Add("The instruction list contains a blank entry.");

        var instructions = rawInstructions.Where(i => !string.IsNullOrWhiteSpace(i)).Select(DroneInstruction.Parse).ToList();

        foreach (var instruction in instructions)
            errors.AddRange(CheckFormat(instruction));

        if (instructions.Any(i => string.Equals(i.Name, DroneInstruction.StartFlight, StringComparison.OrdinalIgnoreCase)))
            errors.AddRange(CheckMission(instructions));

        return new InstructionValidation(errors);
    }

    private IEnumerable<string> CheckFormat(DroneInstruction instruction)
    {
        var name = instruction.Name;
        var args = instruction.Arguments;

        if (args is null)
            yield break;

        if (name.Equals("setDestinationObject", StringComparison.OrdinalIgnoreCase) && !ObjectIdRegex().IsMatch(args))
            yield return $"'{instruction.Raw}': the object ID must match [A-Z]{{3}}[0-9]+[A-Z]{{2}}.";

        if (name.Equals("setOwner", StringComparison.OrdinalIgnoreCase) && args.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length != 2)
            yield return $"'{instruction.Raw}': the owner must be exactly two words, a first name and a surname.";

        if (name.Equals("setLed", StringComparison.OrdinalIgnoreCase) && !HexColourRegex().IsMatch(args))
            yield return $"'{instruction.Raw}': the LED colour must be a HEX code in the #000000 format.";

        if (!name.Equals("set", StringComparison.OrdinalIgnoreCase))
            yield break;

        if (instruction.AsLandingSector() is { } sector)
        {
            if (sector.Column < 1 || sector.Column > gridColumns || sector.Row < 1 || sector.Row > gridRows)
                yield return $"'{instruction.Raw}': the map has {gridColumns} columns and {gridRows} rows, so that sector does not exist.";

            yield break;
        }

        if (AltitudeRegex().Match(args) is { Success: true } altitude && (int.Parse(altitude.Groups[1].Value) is < 1 or > 100))
            yield return $"'{instruction.Raw}': the altitude must be between 1m and 100m.";

        if (PowerRegex().Match(args) is { Success: true } power && int.Parse(power.Groups[1].Value) > 100)
            yield return $"'{instruction.Raw}': the engine power must be between 0% and 100%.";
    }

    private IEnumerable<string> CheckMission(IReadOnlyList<DroneInstruction> instructions)
    {
        var sectors = instructions.Select(i => i.AsLandingSector()).Where(s => s is not null).Select(s => s!.Value).ToList();

        if (sectors.Count == 0)
            yield return $"The list starts a flight but never sets the landing sector. It must contain {RequiredLandingSector}.";
        else if (sectors.Count > 1)
            yield return $"The list sets the landing sector {sectors.Count} times. Set it once, as {RequiredLandingSector}.";
        else if (sectors[0] != (damColumn, damRow))
            yield return $"The landing sector is set({sectors[0].Column},{sectors[0].Row}), but the dam is in column {damColumn}, row {damRow}. The charge must land on {RequiredLandingSector}.";

        var destinations = instructions
            .Where(i => i.Name.Equals("setDestinationObject", StringComparison.OrdinalIgnoreCase) && i.Arguments is not null)
            .Select(i => i.Arguments!)
            .ToList();

        if (destinations.Count == 0)
            yield return $"The list starts a flight but never sets the destination object. It must contain {RequiredDestinationObject}.";
        else if (destinations.Any(d => !d.Equals(targetObjectId, StringComparison.OrdinalIgnoreCase)))
            yield return $"The destination object must stay {targetObjectId} — that registration is what makes the flight look like the mission it is supposed to be.";

        // Documented as required before the flight starts, and every list is sent as a complete
        // mission, so a missing altitude is a local error rather than a wasted submission.
        if (!instructions.Any(i => i.Name.Equals("set", StringComparison.OrdinalIgnoreCase) && i.Arguments is not null && AltitudeRegex().IsMatch(i.Arguments)))
            yield return "The list starts a flight but never sets the flight altitude, which the manual requires beforehand.";
    }

    [GeneratedRegex(@"^[A-Z]{3}[0-9]+[A-Z]{2}$")]
    private static partial Regex ObjectIdRegex();

    [GeneratedRegex(@"^#[0-9A-Fa-f]{6}$")]
    private static partial Regex HexColourRegex();

    [GeneratedRegex(@"^(\d{1,3})\s*m$", RegexOptions.IgnoreCase)]
    private static partial Regex AltitudeRegex();

    [GeneratedRegex(@"^(\d{1,3})\s*%$")]
    private static partial Regex PowerRegex();
}
