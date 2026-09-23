using System.Text.Json.Nodes;
using _04_03_zadanie.City;

namespace _04_03_zadanie.Mission;

public sealed record GuardVerdict(bool Accepted, int Cost, string Reason, IReadOnlyList<Coordinate>? Path = null)
{
    public static GuardVerdict Accept(int cost, string reason, IReadOnlyList<Coordinate>? path = null) => new(true, cost, reason, path);

    public static GuardVerdict Reject(string reason) => new(false, 0, reason);

    public override string ToString() => Accepted ? $"OK, {Cost} pts: {Reason}" : $"REJECTED: {Reason}";
}

/// <summary>
/// Judges every action before it leaves for the hub. A rejection here costs nothing; the same mistake
/// on the hub costs the points it spends, a unit that can never re-board, or the whole operation when
/// reset re-rolls the partisan. The rules are the ones help states plus the arithmetic of the budget,
/// so the guard knows nothing about where the human might be.
/// </summary>
public sealed class ActionGuard(CityMap map, CostTable costs)
{
    private static readonly HashSet<string> ReadOnlyActions = new(StringComparer.Ordinal)
    {
        "help", "getMap", "searchSymbol", "getLogs", "getObjects", "expenses", "actionCost"
    };

    /// <summary>Reset wipes the board and re-rolls the partisan; it is never part of a plan, so it passes only when asked for by hand.</summary>
    public bool AllowReset { get; init; }

    public GuardVerdict Check(JsonObject answer, OperationState state)
    {
        var action = answer["action"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(action))
            return GuardVerdict.Reject("the answer has no action");

        if (ReadOnlyActions.Contains(action))
            return GuardVerdict.Accept(0, "read-only");

        return action switch
        {
            "reset" => AllowReset
                ? GuardVerdict.Accept(0, "reset allowed by hand")
                : GuardVerdict.Reject("reset wipes the board and re-rolls the partisan; it needs an explicit --allow-reset"),
            "create" => CheckCreate(answer, state),
            "move" => CheckMove(answer, state),
            "inspect" => CheckInspect(answer, state),
            "dismount" => CheckDismount(answer, state),
            "callHelicopter" => CheckHelicopter(answer, state),
            _ => GuardVerdict.Reject($"unknown action '{action}'; help lists nothing of that name")
        };
    }

    private GuardVerdict CheckCreate(JsonObject answer, OperationState state)
    {
        if (state.NextFreeSpawnSlot() is null)
            return GuardVerdict.Reject($"every spawn slot ({string.Join(", ", state.SpawnSlots)}) has a unit on it; move one away first");

        switch (answer["type"]?.GetValue<string>())
        {
            case "scout":
                if (state.ScoutsUsed + 1 > state.MaxScouts)
                    return GuardVerdict.Reject($"scout limit reached ({state.ScoutsUsed}/{state.MaxScouts})");
                return WithinBudget(costs.ScoutCreate, "new scout at the spawn slot", state);

            case "transporter":
                var passengers = ReadInt(answer, "passengers");
                if (passengers is null or < 1 or > 4)
                    return GuardVerdict.Reject("a transporter needs 'passengers' between 1 and 4");
                if (state.TransportersUsed + 1 > state.MaxTransporters)
                    return GuardVerdict.Reject($"transporter limit reached ({state.TransportersUsed}/{state.MaxTransporters})");
                if (state.ScoutsUsed + passengers.Value > state.MaxScouts)
                    return GuardVerdict.Reject($"{passengers} passengers would exceed the scout limit ({state.ScoutsUsed}/{state.MaxScouts} used)");
                return WithinBudget(costs.Transporter(passengers.Value), $"transporter with {passengers} scouts aboard", state);

            default:
                return GuardVerdict.Reject("'type' must be 'transporter' or 'scout'");
        }
    }

    private GuardVerdict CheckMove(JsonObject answer, OperationState state)
    {
        var unit = state.FindUnit(answer["object"]?.GetValue<string>());
        if (unit is null)
            return GuardVerdict.Reject("'object' is not a known unit; read the state first");
        if (!Coordinate.TryParse(answer["where"]?.GetValue<string>(), out var target))
            return GuardVerdict.Reject("'where' must be a field A1..K11");
        if (target == unit.Position)
            return GuardVerdict.Reject($"the {unit.Type} already stands on {target}");

        if (unit.Type == UnitType.Transporter)
        {
            if (!map.IsRoad(target))
                return GuardVerdict.Reject($"{target} is '{map[target].Kind.Label}', not a street; transporters drive on streets only");

            var road = Pathfinder.RoadPath(map, unit.Position, target);
            if (road is null)
                return GuardVerdict.Reject($"no street connects {unit.Position} with {target}");

            return WithinBudget(costs.TransporterMove(road.Count), $"transporter {unit.Position} -> {target}, {road.Count} fields by street", state, road);
        }

        var walk = Pathfinder.ScoutPath(unit.Position, target);
        return WithinBudget(costs.ScoutMove(walk.Count), $"scout {unit.Position} -> {target}, {walk.Count} fields on foot", state, walk);
    }

    private GuardVerdict CheckInspect(JsonObject answer, OperationState state)
    {
        var unit = state.FindUnit(answer["object"]?.GetValue<string>());
        if (unit is null)
            return GuardVerdict.Reject("'object' is not a known unit; read the state first");
        if (unit.Type != UnitType.Scout)
            return GuardVerdict.Reject("only a scout can inspect; a transporter sees nothing");

        return WithinBudget(costs.Inspect, $"inspect {unit.Position} ('{map[unit.Position].Kind.Label}')", state);
    }

    private GuardVerdict CheckDismount(JsonObject answer, OperationState state)
    {
        var unit = state.FindUnit(answer["object"]?.GetValue<string>());
        if (unit is null)
            return GuardVerdict.Reject("'object' is not a known unit; read the state first");
        if (unit.Type != UnitType.Transporter)
            return GuardVerdict.Reject("only a transporter can dismount scouts");

        var passengers = ReadInt(answer, "passengers");
        if (passengers is null or < 1 or > 4)
            return GuardVerdict.Reject("'passengers' must be between 1 and 4");
        if (unit.PassengersAboard is { } aboard && passengers.Value > aboard)
            return GuardVerdict.Reject($"only {aboard} scouts aboard, cannot dismount {passengers}");

        var freeAround = unit.Position.Neighbours().Count(field => !state.IsOccupied(field));
        if (freeAround < passengers.Value)
            return GuardVerdict.Reject($"only {freeAround} free fields around {unit.Position}, cannot place {passengers} scouts");

        return WithinBudget(costs.Dismount, $"{passengers} scouts step off at {unit.Position}", state);
    }

    private GuardVerdict CheckHelicopter(JsonObject answer, OperationState state)
    {
        if (!Coordinate.TryParse(answer["destination"]?.GetValue<string>(), out var destination))
            return GuardVerdict.Reject("'destination' must be a field A1..K11");
        if (!state.HumanFound)
            return GuardVerdict.Reject("no scout has confirmed a human yet; the hub would refuse the call");
        if (state.HumanFoundAt is null)
            return GuardVerdict.Reject("the human is confirmed but the field is unknown; read the state first");
        if (state.HumanFoundAt.Value != destination)
            return GuardVerdict.Reject($"the human was confirmed at {state.HumanFoundAt}, not {destination}");

        return WithinBudget(costs.CallHelicopter, $"helicopter to {destination}, where the human was confirmed", state);
    }

    private static GuardVerdict WithinBudget(int cost, string what, OperationState state, IReadOnlyList<Coordinate>? path = null) =>
        cost > state.PointsLeft
            ? GuardVerdict.Reject($"{what} costs {cost} points, only {state.PointsLeft} left")
            : GuardVerdict.Accept(cost, what, path);

    private static int? ReadInt(JsonObject answer, string field) => answer[field] switch
    {
        JsonValue value when value.TryGetValue<int>(out var number) => number,
        JsonValue value when value.TryGetValue<string>(out var text) && int.TryParse(text, out var parsed) => parsed,
        _ => null
    };
}
