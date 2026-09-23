using System.Text.Json.Nodes;
using _04_03_zadanie.City;

namespace _04_03_zadanie.Mission;

/// <summary>What the operation should do next: one action for the hub, or a reason to stop.</summary>
public abstract record Decision(string Why)
{
    /// <summary>An action to send. Summary is the human-readable line ("move scout ... C8 -> C10"), Why the reasoning behind it.</summary>
    public sealed record Act(JsonObject Answer, string Summary, string Why) : Decision(Why);

    /// <summary>A scout has confirmed the human; the only thing left is the helicopter, which the operator calls by hand.</summary>
    public sealed record Finished(Coordinate HumanAt) : Decision($"human confirmed at {HumanAt}");

    /// <summary>Nothing sensible can be sent; the reason says what to look at.</summary>
    public sealed record Stuck(string Reason) : Decision(Reason);
}

/// <summary>
/// Picks the next action from the live board, one at a time, re-deciding after every observation.
/// The order of preference follows the prices: inspect where a scout already stands (1 point),
/// let scouts step off a parked transporter (0), walk a scout that is already near its block (7 a
/// field), drive a transporter that still carries scouts (1 a field), and only then buy new units.
/// A scout on foot is sent to a cluster only when walking there is no dearer than delivering a fresh
/// scout by transporter, so a scout whose block is done does not trudge across town for 7 a field.
/// </summary>
public static class Tactician
{
    private sealed record OpenCluster(Cluster Cluster, IReadOnlyList<Coordinate> Remaining);

    public static Decision Decide(OperationState state, CityMap map, IReadOnlyList<Cluster> clusters, CostTable costs)
    {
        if (state.HumanFound)
            return state.HumanFoundAt is { } at
                ? new Decision.Finished(at)
                : new Decision.Stuck("the hub says the human is found but does not say where; read the state again");

        var open = clusters
            .Select(cluster => new OpenCluster(cluster, cluster.Fields.Where(field => !state.Inspected.Contains(field)).ToList()))
            .Where(cluster => cluster.Remaining.Count > 0)
            .ToList();

        if (open.Count == 0)
            return new Decision.Stuck("every target field has been inspected and nobody was found; the reading of the signal (target symbol) may be wrong");

        var scouts = state.Scouts.ToList();
        var carriers = state.Transporters.Where(transporter => (transporter.PassengersAboard ?? 0) > 0).ToList();

        // 1. A scout already standing on an uninspected target field: one point, possibly the end.
        foreach (var scout in scouts)
        {
            var here = open.FirstOrDefault(cluster => cluster.Remaining.Contains(scout.Position));
            if (here is not null)
                return new Decision.Act(Inspect(scout), $"inspect {scout.Position} by scout {scout.ShortId}",
                    $"the scout stands on an uninspected field of {here.Cluster.Name} ({here.Remaining.Count} left there)");
        }

        // 2. Which scout on foot works which cluster: nearest first, one scout per cluster, and only
        //    when walking in is no dearer than bringing a fresh scout by transporter.
        var delivery = open.ToDictionary(cluster => cluster.Cluster.Name, cluster => DeliveryCost(cluster, carriers, state, map, costs));
        var assignments = new Dictionary<string, OpenCluster>(StringComparer.OrdinalIgnoreCase);
        var taken = new HashSet<string>();

        var candidates = scouts
            .SelectMany(scout => open.Select(cluster => (Scout: scout, Cluster: cluster, Walk: costs.ScoutMove(cluster.Remaining.Min(field => scout.Position.DistanceTo(field))))))
            .Where(candidate => candidate.Walk <= delivery[candidate.Cluster.Cluster.Name])
            .OrderBy(candidate => candidate.Walk);

        foreach (var candidate in candidates)
            if (!assignments.ContainsKey(candidate.Scout.Id) && taken.Add(candidate.Cluster.Cluster.Name))
                assignments[candidate.Scout.Id] = candidate.Cluster;

        var unassigned = open.Where(cluster => !taken.Contains(cluster.Cluster.Name)).ToList();

        // 3. A loaded transporter already parked at a stop of a cluster nobody works: scouts step off for free.
        foreach (var transporter in carriers)
        {
            var here = unassigned.FirstOrDefault(cluster => cluster.Cluster.Stops.Contains(transporter.Position));
            if (here is not null)
                return new Decision.Act(Dismount(transporter, 1), $"dismount 1 scout from {transporter.ShortId} at {transporter.Position}",
                    $"{transporter.Position} is a stop of {here.Cluster.Name} ({here.Remaining.Count} fields left) and no scout works it yet");
        }

        // 4. A scout on foot continues its sweep: the cheapest next step among the scouts with work.
        var walks = assignments
            .Select(pair => (Scout: state.FindUnit(pair.Key)!, Cluster: pair.Value, Sweep: SearchPlanner.SweepFrom(state.FindUnit(pair.Key)!.Position, pair.Value.Remaining, costs)))
            .Where(walk => walk.Sweep.Count > 0)
            .OrderBy(walk => walk.Sweep[0].Walk)
            .ToList();

        if (walks.Count > 0)
        {
            var (scout, cluster, sweep) = walks[0];
            var step = sweep[0];
            return new Decision.Act(Move(scout, step.Field), $"move scout {scout.ShortId} {scout.Position} -> {step.Field} ({step.Walk} fields, {costs.ScoutMove(step.Walk)} pts)",
                $"the scout sweeps {cluster.Cluster.Name}: {cluster.Remaining.Count} fields left, next {string.Join(" ", sweep.Select(s => s.Field))}");
        }

        // 5. A loaded transporter drives to the cluster that is cheapest to search from where it stands.
        if (unassigned.Count > 0)
        {
            (Unit Transporter, ClusterVisit Visit, int Cost)? best = null;
            foreach (var transporter in carriers)
                foreach (var cluster in unassigned)
                {
                    var visit = SearchPlanner.CheapestVisit(map, cluster.Cluster, cluster.Remaining, transporter.Position, costs);
                    if (visit is null)
                        continue;

                    var cost = costs.TransporterMove(visit.RoadSteps) + costs.ScoutMove(visit.WalkSteps + SearchPlanner.LandingWalk) + costs.Inspect * visit.Sweep.Count;
                    if (best is null || cost < best.Value.Cost)
                        best = (transporter, visit, cost);
                }

            if (best is { } chosen)
                return new Decision.Act(Move(chosen.Transporter, chosen.Visit.Stop), $"move transporter {chosen.Transporter.ShortId} {chosen.Transporter.Position} -> {chosen.Visit.Stop} ({chosen.Visit.RoadSteps} fields, {costs.TransporterMove(chosen.Visit.RoadSteps)} pts)",
                    $"{chosen.Visit.Cluster.Name} is the cheapest cluster left to search from there: about {chosen.Cost} pts to sweep it all");
        }

        // 6. Nobody can help: buy a transporter with as many scouts as the plan for what is left wants.
        if (unassigned.Count == 0)
            return new Decision.Stuck("every open cluster has a scout assigned but none of them can move; read the state again");
        if (state.TransportersUsed >= state.MaxTransporters)
            return new Decision.Stuck($"transporter limit reached ({state.TransportersUsed}/{state.MaxTransporters}) with {unassigned.Count} cluster(s) still unsearched");
        if (state.ScoutsUsed >= state.MaxScouts)
            return new Decision.Stuck($"scout limit reached ({state.ScoutsUsed}/{state.MaxScouts}) with {unassigned.Count} cluster(s) still unsearched");

        var plan = SearchPlanner.PlanAll(map, unassigned.Select(cluster => cluster.Cluster).ToList(), costs, state)[0];
        var passengers = Math.Min(plan.Legs[0].Passengers, state.MaxScouts - state.ScoutsUsed);
        return new Decision.Act(Create(passengers), $"create transporter with {passengers} scout(s) ({costs.Transporter(passengers)} pts)",
            $"no unit can reach {string.Join(", ", unassigned.Select(cluster => cluster.Cluster.Name))}; plan for the rest: {plan.Summary}");
    }

    /// <summary>What it costs to bring a fresh scout to the cluster: the cheapest loaded transporter, or a new one when none is loaded.</summary>
    private static int DeliveryCost(OpenCluster cluster, IReadOnlyList<Unit> carriers, OperationState state, CityMap map, CostTable costs)
    {
        var landing = costs.ScoutMove(SearchPlanner.LandingWalk);
        var best = int.MaxValue;

        foreach (var transporter in carriers)
            if (SearchPlanner.CheapestVisit(map, cluster.Cluster, cluster.Remaining, transporter.Position, costs) is { } visit)
                best = Math.Min(best, costs.TransporterMove(visit.RoadSteps) + landing);

        if (best == int.MaxValue
            && state.TransportersUsed < state.MaxTransporters
            && state.ScoutsUsed < state.MaxScouts
            && state.NextFreeSpawnSlot() is { } spawn
            && SearchPlanner.CheapestVisit(map, cluster.Cluster, cluster.Remaining, spawn, costs) is { } fresh)
            best = costs.Transporter(1) + costs.TransporterMove(fresh.RoadSteps) + landing;

        return best;
    }

    private static JsonObject Inspect(Unit scout) => new() { ["action"] = "inspect", ["object"] = scout.Id };

    private static JsonObject Move(Unit unit, Coordinate where) => new() { ["action"] = "move", ["object"] = unit.Id, ["where"] = where.ToString() };

    private static JsonObject Dismount(Unit transporter, int passengers) => new() { ["action"] = "dismount", ["object"] = transporter.Id, ["passengers"] = passengers };

    private static JsonObject Create(int passengers) => new() { ["action"] = "create", ["type"] = "transporter", ["passengers"] = passengers };
}
