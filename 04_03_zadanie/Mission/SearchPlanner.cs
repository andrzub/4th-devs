using System.Text;
using _04_03_zadanie.City;

namespace _04_03_zadanie.Mission;

/// <summary>One field of a sweep: walk this many fields from the previous position, then inspect.</summary>
public sealed record SweepStep(Coordinate Field, int Walk);

/// <summary>One cluster searched by one scout who steps off the transporter parked at the stop.</summary>
public sealed record ClusterVisit(Cluster Cluster, Coordinate Stop, IReadOnlyList<Coordinate> Road, Coordinate Landing, IReadOnlyList<SweepStep> Sweep)
{
    public int RoadSteps => Road.Count;

    public int WalkSteps => Sweep.Sum(step => step.Walk);
}

/// <summary>One transporter, created with exactly as many scouts aboard as clusters it will visit.</summary>
public sealed record ConvoyLeg(Coordinate Spawn, IReadOnlyList<ClusterVisit> Visits)
{
    public int Passengers => Visits.Count;
}

public sealed record SearchPlan(IReadOnlyList<ConvoyLeg> Legs, int WorstCase, int LandingReserve, double Expected, int Budget)
{
    /// <summary>The whole search paid to the last field, plus the walk from where dismount really puts a scout to the block.</summary>
    public bool FitsBudget => WorstCase + LandingReserve <= Budget;

    public IEnumerable<ClusterVisit> Visits => Legs.SelectMany(leg => leg.Visits);

    public string Summary =>
        $"{string.Join(" -> ", Visits.Select(visit => visit.Cluster.Name))} | legs {string.Join("+", Legs.Select(leg => leg.Passengers))} | worst {WorstCase} (+{LandingReserve} reserve) | expected {Expected:F1}{(FitsBudget ? string.Empty : " | OVER BUDGET")}";

    public string Describe(CostTable costs)
    {
        var text = new StringBuilder();
        text.AppendLine($"Plan: {Legs.Count} transporter(s), {Legs.Sum(leg => leg.Passengers)} scouts, order {string.Join(" -> ", Visits.Select(visit => visit.Cluster.Name))}");

        var running = 0;
        foreach (var (leg, index) in Legs.Select((leg, index) => (leg, index + 1)))
        {
            running += costs.Transporter(leg.Passengers);
            text.AppendLine($"  leg {index}: transporter spawns at {leg.Spawn} with {leg.Passengers} scout(s) aboard, {costs.Transporter(leg.Passengers)} pts (running {running})");

            foreach (var visit in leg.Visits)
            {
                running += costs.TransporterMove(visit.RoadSteps);
                text.AppendLine($"    drive to {visit.Stop}: {visit.RoadSteps} fields, {costs.TransporterMove(visit.RoadSteps)} pts (running {running}); dismount 1 scout, expected landing {visit.Landing}");

                var walked = new StringBuilder();
                foreach (var step in visit.Sweep)
                {
                    running += costs.ScoutMove(step.Walk) + costs.Inspect;
                    walked.Append($" {step.Field}(+{step.Walk})");
                }

                text.AppendLine($"    sweep {visit.Cluster.Name}:{walked} = {visit.WalkSteps} fields on foot, {costs.ScoutMove(visit.WalkSteps)} pts + {visit.Sweep.Count} inspections (running {running})");
            }
        }

        text.AppendLine($"  worst case {WorstCase} pts + landing reserve {LandingReserve} = {WorstCase + LandingReserve} of {Budget}{(FitsBudget ? string.Empty : "  <-- OVER BUDGET")}");
        text.Append($"  expected {Expected:F1} pts (partisan equally likely on every searched field)");
        return text.ToString();
    }
}

/// <summary>
/// Turns the clusters into an order of visits that spends the least in expectation while the worst
/// case still fits the budget. Every order of clusters and every way of splitting it between
/// transporters is priced exhaustively: the search stops the moment a scout finds the human, so a
/// cheap first stop beats a short total tour, and a fresh transporter per cluster can beat one convoy.
/// </summary>
public static class SearchPlanner
{
    /// <summary>
    /// Dismount puts a scout on open ground next to the vehicle, not on the block: observed at C9, where
    /// the scout landed on C8 and had two fields to walk to C10. Every visit is budgeted for that walk.
    /// </summary>
    public const int LandingWalk = 2;

    private const int ExhaustiveSweepLimit = 8;

    public static SearchPlan Plan(CityMap map, IReadOnlyList<Cluster> clusters, CostTable costs, OperationState state, IReadOnlyDictionary<Coordinate, double>? weights = null) =>
        PlanAll(map, clusters, costs, state, weights)[0];

    /// <summary>Every viable plan, best first: those fitting the budget, then by expected cost, then by worst case.</summary>
    public static IReadOnlyList<SearchPlan> PlanAll(CityMap map, IReadOnlyList<Cluster> clusters, CostTable costs, OperationState state, IReadOnlyDictionary<Coordinate, double>? weights = null)
    {
        if (clusters.Count == 0)
            throw new InvalidOperationException("Nothing to search: no cluster carries the target symbol.");

        var sweeps = new Dictionary<(string Cluster, Coordinate Landing), IReadOnlyList<SweepStep>>();
        var plans = new List<SearchPlan>();

        foreach (var order in Permutations(clusters))
            foreach (var grouping in Compositions(order.Count))
                if (Build(map, order, grouping, costs, state, weights, sweeps) is { } plan)
                    plans.Add(plan);

        if (plans.Count == 0)
            throw new InvalidOperationException("No cluster can be reached from a spawn slot by street.");

        return plans.OrderByDescending(plan => plan.FitsBudget).ThenBy(plan => plan.Expected).ThenBy(plan => plan.WorstCase).ToList();
    }

    /// <summary>
    /// The cheapest way for one scout standing at <paramref name="start"/> to inspect every field, judged
    /// by the weighted sum of the running cost at each inspection (the expected cost), then by the total.
    /// Used both for planning and for re-planning once the hub reveals where a scout actually landed.
    /// </summary>
    public static IReadOnlyList<SweepStep> SweepFrom(Coordinate start, IEnumerable<Coordinate> fields, CostTable costs, IReadOnlyDictionary<Coordinate, double>? weights = null)
    {
        var remaining = fields.Distinct().ToList();
        if (remaining.Count == 0)
            return [];
        if (remaining.Count > ExhaustiveSweepLimit)
            return GreedySweep(start, remaining);

        IReadOnlyList<SweepStep>? best = null;
        var bestScore = (Weighted: double.MaxValue, Total: int.MaxValue);

        foreach (var order in Permutations(remaining))
        {
            var steps = new List<SweepStep>(remaining.Count);
            var position = start;
            var running = 0;
            var weighted = 0.0;

            foreach (var field in order)
            {
                var walk = position.DistanceTo(field);
                running += costs.ScoutMove(walk) + costs.Inspect;
                weighted += Weight(field, weights) * running;
                steps.Add(new SweepStep(field, walk));
                position = field;
            }

            var score = (weighted, running);
            if (score.CompareTo(bestScore) < 0)
            {
                bestScore = score;
                best = steps;
            }
        }

        return best!;
    }

    private static SearchPlan? Build(CityMap map, IReadOnlyList<Cluster> order, IReadOnlyList<int> grouping, CostTable costs, OperationState state, IReadOnlyDictionary<Coordinate, double>? weights, Dictionary<(string, Coordinate), IReadOnlyList<SweepStep>> sweeps)
    {
        var occupied = new HashSet<Coordinate>(state.Units.Select(unit => unit.Position));
        var allFields = order.SelectMany(cluster => cluster.Fields).ToList();
        var totalWeight = allFields.Sum(field => Weight(field, weights));
        if (totalWeight <= 0)
        {
            weights = null;
            totalWeight = allFields.Count;
        }

        var legs = new List<ConvoyLeg>();
        var total = 0;
        var expected = 0.0;
        var next = 0;

        foreach (var size in grouping)
        {
            Coordinate? spawn = null;
            foreach (var slot in state.SpawnSlots)
                if (!occupied.Contains(slot))
                {
                    spawn = slot;
                    break;
                }

            if (spawn is null)
                return null;

            total += costs.Transporter(size);
            var position = spawn.Value;
            var visits = new List<ClusterVisit>();

            for (var i = 0; i < size; i++, next++)
            {
                var visit = BestVisit(map, order[next], position, costs, weights, sweeps);
                if (visit is null)
                    return null;

                total += costs.TransporterMove(visit.RoadSteps);
                foreach (var step in visit.Sweep)
                {
                    total += costs.ScoutMove(step.Walk) + costs.Inspect;
                    expected += Weight(step.Field, weights) / totalWeight * total;
                }

                position = visit.Stop;
                visits.Add(visit);
            }

            occupied.Add(position);
            legs.Add(new ConvoyLeg(spawn.Value, visits));
        }

        var reserve = costs.ScoutMove(LandingWalk) * legs.Sum(leg => leg.Visits.Count);
        return new SearchPlan(legs, total, reserve, expected, state.Budget);
    }

    /// <summary>
    /// The cheapest way to search what is left of a cluster from where a transporter stands now: which
    /// stop to drive to and, from the block next to it, in what order to inspect the remaining fields.
    /// Null when no street leads there.
    /// </summary>
    public static ClusterVisit? CheapestVisit(CityMap map, Cluster cluster, IReadOnlyCollection<Coordinate> remaining, Coordinate from, CostTable costs) =>
        BestVisit(map, cluster, remaining, from, costs, null, new Dictionary<(string, Coordinate), IReadOnlyList<SweepStep>>());

    private static ClusterVisit? BestVisit(CityMap map, Cluster cluster, Coordinate from, CostTable costs, IReadOnlyDictionary<Coordinate, double>? weights, Dictionary<(string, Coordinate), IReadOnlyList<SweepStep>> sweeps) =>
        BestVisit(map, cluster, cluster.Fields, from, costs, weights, sweeps);

    private static ClusterVisit? BestVisit(CityMap map, Cluster cluster, IReadOnlyCollection<Coordinate> remaining, Coordinate from, CostTable costs, IReadOnlyDictionary<Coordinate, double>? weights, Dictionary<(string, Coordinate), IReadOnlyList<SweepStep>> sweeps)
    {
        ClusterVisit? best = null;
        var bestScore = (Weighted: double.MaxValue, Total: int.MaxValue);

        foreach (var stop in cluster.Stops)
        {
            var road = Pathfinder.RoadPath(map, from, stop);
            if (road is null)
                continue;

            var roadCost = costs.TransporterMove(road.Count);
            foreach (var landing in cluster.FieldsTouching(stop))
            {
                if (!sweeps.TryGetValue((cluster.Name, landing), out var sweep))
                    sweeps[(cluster.Name, landing)] = sweep = SweepFrom(landing, remaining, costs, weights);

                var running = roadCost;
                var weighted = 0.0;
                foreach (var step in sweep)
                {
                    running += costs.ScoutMove(step.Walk) + costs.Inspect;
                    weighted += Weight(step.Field, weights) * running;
                }

                var score = (weighted, running);
                if (score.CompareTo(bestScore) < 0)
                {
                    bestScore = score;
                    best = new ClusterVisit(cluster, stop, road, landing, sweep);
                }
            }
        }

        return best;
    }

    private static IReadOnlyList<SweepStep> GreedySweep(Coordinate start, List<Coordinate> remaining)
    {
        var steps = new List<SweepStep>();
        var position = start;
        while (remaining.Count > 0)
        {
            var nearest = remaining.MinBy(field => position.DistanceTo(field));
            steps.Add(new SweepStep(nearest, position.DistanceTo(nearest)));
            remaining.Remove(nearest);
            position = nearest;
        }

        return steps;
    }

    private static double Weight(Coordinate field, IReadOnlyDictionary<Coordinate, double>? weights) =>
        weights is null ? 1.0 : weights.GetValueOrDefault(field, 0.0);

    private static IEnumerable<IReadOnlyList<T>> Permutations<T>(IReadOnlyList<T> items)
    {
        if (items.Count <= 1)
        {
            yield return items;
            yield break;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var head = items[i];
            var rest = items.Where((_, index) => index != i).ToList();
            foreach (var tail in Permutations(rest))
                yield return new[] { head }.Concat(tail).ToList();
        }
    }

    /// <summary>All ways of splitting n ordered visits into consecutive groups, one transporter per group.</summary>
    private static IEnumerable<IReadOnlyList<int>> Compositions(int n)
    {
        if (n == 0)
        {
            yield return Array.Empty<int>();
            yield break;
        }

        for (var first = 1; first <= n; first++)
            foreach (var rest in Compositions(n - first))
                yield return new[] { first }.Concat(rest).ToList();
    }
}
