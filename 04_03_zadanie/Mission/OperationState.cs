using System.Text.Json.Nodes;
using _04_03_zadanie.City;

namespace _04_03_zadanie.Mission;

public enum UnitType
{
    Transporter,
    Scout
}

/// <summary>A unit on the board. Passengers aboard a transporter are null until a reply or the ledger has said how many there are.</summary>
public sealed record Unit(string Id, UnitType Type, Coordinate Position, int? PassengersAboard = null)
{
    public string ShortId => Id.Length > 8 ? Id[..8] : Id;

    public override string ToString() =>
        Type == UnitType.Transporter
            ? $"Transporter {ShortId} @ {Position} ({(PassengersAboard is { } aboard ? $"{aboard} aboard" : "load unknown")})"
            : $"Scout {ShortId} @ {Position}";
}

/// <summary>One inspection as getLogs reports it: who looked, where, and the prose the hub wrote about it.</summary>
public sealed record LogEntry(string Scout, Coordinate? Field, string Message);

/// <summary>
/// What the operation knows about the board at a given moment: units, points, limits, inspected fields
/// and whether a scout has already confirmed the human. Positions come from getObjects, points and
/// flags from the preview backend, inspections from getLogs; the guard judges every action against
/// this and the tactician picks the next one from it.
/// </summary>
public sealed class OperationState(IReadOnlyList<Coordinate> spawnSlots)
{
    public IReadOnlyList<Coordinate> SpawnSlots { get; } = spawnSlots;

    public int Budget { get; init; } = 300;

    public int MaxTransporters { get; init; } = 4;

    public int MaxScouts { get; init; } = 8;

    public int PointsUsed { get; set; }

    public int PointsLeft => Budget - PointsUsed;

    public int TransportersUsed { get; set; }

    public int ScoutsUsed { get; set; }

    public bool HumanFound { get; set; }

    public Coordinate? HumanFoundAt { get; set; }

    public string MissionFlag { get; set; } = string.Empty;

    public List<Unit> Units { get; } = [];

    public HashSet<Coordinate> Inspected { get; } = [];

    /// <summary>Everything the last getLogs read returned.</summary>
    public List<LogEntry> Logs { get; } = [];

    /// <summary>The entries of that read the ledger had not seen before.</summary>
    public List<LogEntry> FreshLogs { get; } = [];

    public IEnumerable<Unit> Scouts => Units.Where(unit => unit.Type == UnitType.Scout);

    public IEnumerable<Unit> Transporters => Units.Where(unit => unit.Type == UnitType.Transporter);

    public Unit? FindUnit(string? id) =>
        id is null ? null : Units.FirstOrDefault(unit => unit.Id.Equals(id.Trim(), StringComparison.OrdinalIgnoreCase));

    public bool IsOccupied(Coordinate field) => Units.Any(unit => unit.Position == field);

    /// <summary>Where the next created unit appears, or null when every slot has a unit standing on it.</summary>
    public Coordinate? NextFreeSpawnSlot()
    {
        foreach (var slot in SpawnSlots)
            if (!IsOccupied(slot))
                return slot;

        return null;
    }

    public void SetAboard(string transporterId, int? aboard)
    {
        var index = Units.FindIndex(unit => unit.Id.Equals(transporterId, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            Units[index] = Units[index] with { PassengersAboard = aboard };
    }

    /// <summary>The preview backend's pull reply: points and flags, plus the unit list it carries (which lags behind moves).</summary>
    public void ApplyPull(JsonNode reply)
    {
        ApplyStats(reply);
        ApplyObjects(reply);
    }

    public void ApplyStats(JsonNode reply)
    {
        if (reply["stats"] is not JsonObject stats)
            return;

        PointsUsed = stats["action_points_used"]?.GetValue<int>() ?? PointsUsed;
        TransportersUsed = stats["used_transporters"]?.GetValue<int>() ?? TransportersUsed;
        ScoutsUsed = stats["used_scouts"]?.GetValue<int>() ?? ScoutsUsed;
        HumanFound = stats["human_found"]?.GetValue<bool>() ?? false;
        HumanFoundAt = Coordinate.TryParse(stats["human_found_at"]?.GetValue<string>(), out var foundAt) ? foundAt : null;
        MissionFlag = stats["mission_flag"]?.GetValue<string>() ?? string.Empty;
    }

    /// <summary>
    /// A unit list, from getObjects or from the preview backend. getObjects spells the type as "typ",
    /// the backend as "type"; both are read. Passenger counts already known are kept.
    /// </summary>
    public void ApplyObjects(JsonNode reply)
    {
        if (reply["objects"] is not JsonArray objects)
            return;

        var known = Units.ToDictionary(unit => unit.Id, unit => unit.PassengersAboard, StringComparer.OrdinalIgnoreCase);
        Units.Clear();

        foreach (var entry in objects.OfType<JsonObject>())
        {
            var id = entry["id"]?.GetValue<string>();
            var position = (entry["position"] ?? entry["spawn"] ?? entry["where"])?.GetValue<string>();
            var type = (entry["type"] ?? entry["typ"])?.GetValue<string>()?.Trim().ToLowerInvariant() switch
            {
                "transporter" or "transport" => UnitType.Transporter,
                "scout" => UnitType.Scout,
                _ => (UnitType?)null
            };

            if (id is null || type is null || !Coordinate.TryParse(position, out var field))
                continue;

            var aboard = entry["passengers"]?.GetValue<int>() ?? (known.TryGetValue(id, out var remembered) ? remembered : null);
            Units.Add(new Unit(id, type.Value, field, aboard));
        }
    }

    /// <summary>
    /// The getLogs reply. The hub drains the queue on every read, so these are the entries nobody has
    /// seen before; the ledger keeps the full history and marks the fields as inspected.
    /// </summary>
    public void ApplyLogs(JsonNode reply)
    {
        if (reply["logs"] is not JsonArray logs)
            return;

        Logs.Clear();
        Logs.AddRange(ParseLogs(logs));
        foreach (var entry in Logs)
            if (entry.Field is { } field)
                Inspected.Add(field);
    }

    public static IEnumerable<LogEntry> ParseLogs(JsonArray logs)
    {
        foreach (var entry in logs.OfType<JsonObject>())
        {
            var field = Coordinate.TryParse(entry["field"]?.GetValue<string>(), out var parsed) ? parsed : (Coordinate?)null;
            yield return new LogEntry(entry["scout"]?.GetValue<string>() ?? "?", field, entry["msg"]?.GetValue<string>() ?? string.Empty);
        }
    }

    public override string ToString()
    {
        var units = Units.Count == 0 ? "no units" : string.Join(", ", Units.Select(unit => unit.ToString()));
        var human = HumanFound ? $"human confirmed at {HumanFoundAt}" : "human not found yet";
        return $"{PointsUsed}/{Budget} points used, {TransportersUsed}/{MaxTransporters} transporters, {ScoutsUsed}/{MaxScouts} scouts, {human}; {units}";
    }
}
