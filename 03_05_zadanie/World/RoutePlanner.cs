using System.Text;

namespace _03_05_zadanie.World;

public sealed record PlannedRoute(string Departure, IReadOnlyList<string> Instructions, double Fuel, double Food, int Moves)
{
    public string Summary =>
        $"{Departure}: {Moves} moves, fuel {WorldModel.Format(Fuel)}, food {WorldModel.Format(Food)}";
}

public sealed record DepartureOption(string Departure, PlannedRoute? Route, string Note);

public sealed record PlanningReport(PlannedRoute? Best, IReadOnlyList<DepartureOption> Options)
{
    public string Render()
    {
        var sb = new StringBuilder();
        foreach (var option in Options)
            sb.AppendLine(option.Route is null ? $"{option.Departure}: {option.Note}" : $"{option.Route.Summary} — {option.Note}");

        if (Best is null)
            sb.Append("No departure mode reaches the goal within the budget.");
        else
            sb.Append($"Best route: {RouteInstructions.Render(Best.Instructions)}");

        return sb.ToString();
    }
}

/// <summary>
/// Finds the cheapest way through the grid for every departure mode. Two resources drain at
/// different rates, so a single shortest-path cost does not exist: the search keeps every
/// non-dominated (fuel, food, moves) label per tile and only then applies the budget. The vehicle
/// choice is the outcome of that comparison, not an assumption made before it.
/// </summary>
public static class RoutePlanner
{
    private const double Tolerance = 1e-9;

    public static PlanningReport Plan(WorldModel world)
    {
        var options = new List<DepartureOption>();

        foreach (var departure in world.Vehicles.Where(vehicle => vehicle.SelectableAtStart || vehicle.Name == world.WalkMode))
        {
            var affordable = Search(world, departure, world.FuelBudget, world.FoodBudget);
            if (affordable.Count > 0)
            {
                var best = Pick(world, affordable)!;
                options.Add(new DepartureOption(departure.Name, best, Describe(world, best)));
                continue;
            }

            options.Add(new DepartureOption(departure.Name, null, ExplainFailure(world, departure)));
        }

        var winner = options.Where(option => option.Route is not null)
            .Select(option => option.Route!)
            .OrderBy(route => route.Moves)
            .ThenBy(route => Utilisation(world, route))
            .FirstOrDefault();

        return new PlanningReport(winner, options);
    }

    private static string Describe(WorldModel world, PlannedRoute route) =>
        $"{WorldModel.Format(Utilisation(world, route) * 100)}% of the tighter resource is spent";

    private static double Utilisation(WorldModel world, PlannedRoute route) =>
        Math.Max(world.FuelBudget > 0 ? route.Fuel / world.FuelBudget : 0, world.FoodBudget > 0 ? route.Food / world.FoodBudget : 0);

    private static string ExplainFailure(WorldModel world, VehicleProfile departure)
    {
        var stretched = Search(world, departure, world.FuelBudget * 4 + 10, world.FoodBudget * 4 + 10);
        if (stretched.Count == 0)
            return "cannot reach the goal at all — the terrain blocks every path for this mode.";

        var cheapest = stretched
            .OrderBy(label => Math.Max(label.Fuel / Math.Max(world.FuelBudget, Tolerance), label.Food / Math.Max(world.FoodBudget, Tolerance)))
            .First();

        return $"reaches the goal only over budget: {cheapest.Moves} moves would need fuel {WorldModel.Format(cheapest.Fuel)} and food {WorldModel.Format(cheapest.Food)}.";
    }

    private static PlannedRoute? Pick(WorldModel world, List<Label> labels)
    {
        var best = labels
            .OrderBy(label => label.Moves)
            .ThenBy(label => Math.Max(label.Fuel / Math.Max(world.FuelBudget, Tolerance), label.Food / Math.Max(world.FoodBudget, Tolerance)))
            .ThenBy(label => label.Fuel + label.Food)
            .FirstOrDefault();

        return best is null ? null : new PlannedRoute(best.Departure, Reconstruct(best), best.Fuel, best.Food, best.Moves);
    }

    private static List<string> Reconstruct(Label label)
    {
        var steps = new List<string>();
        for (var current = label; current.Parent is not null; current = current.Parent)
            steps.Add(current.Action!);

        steps.Reverse();
        steps.Insert(0, label.Departure);
        return steps;
    }

    private sealed class Label
    {
        public required string Departure { get; init; }
        public required Position Position { get; init; }
        public required bool OnFoot { get; init; }
        public required double Fuel { get; init; }
        public required double Food { get; init; }
        public required int Moves { get; init; }
        public Label? Parent { get; init; }
        public string? Action { get; init; }
    }

    /// <summary>Label-correcting search keeping the non-dominated (fuel, food, moves) triples per tile and mode.</summary>
    private static List<Label> Search(WorldModel world, VehicleProfile departure, double fuelCap, double foodCap)
    {
        var walking = string.Equals(departure.Name, world.WalkMode, StringComparison.OrdinalIgnoreCase);
        var start = new Label
        {
            Departure = departure.Name,
            Position = world.Map.Start,
            OnFoot = walking,
            Fuel = 0,
            Food = 0,
            Moves = 0
        };

        var frontier = new Queue<Label>();
        var settled = new Dictionary<(Position Position, bool OnFoot), List<Label>>();
        var goalLabels = new List<Label>();
        var maxMoves = world.Map.Rows * world.Map.Cols * 2;

        Offer(start);

        while (frontier.Count > 0)
        {
            var label = frontier.Dequeue();

            if (label.Position == world.Map.Goal)
                continue;

            if (label.Moves >= maxMoves)
                continue;

            var mode = label.OnFoot ? world.Walk : departure;

            if (!label.OnFoot && world.DismountAllowed)
            {
                Offer(new Label
                {
                    Departure = departure.Name,
                    Position = label.Position,
                    OnFoot = true,
                    Fuel = label.Fuel,
                    Food = label.Food,
                    Moves = label.Moves,
                    Parent = label,
                    Action = RouteInstructions.Dismount
                });
            }

            foreach (var direction in RouteInstructions.Directions)
            {
                RouteInstructions.TryOffset(direction, out var offset);
                var target = RouteInstructions.Apply(label.Position, offset);
                if (!world.CanEnter(mode, target, out _))
                    continue;

                var (fuelCost, foodCost) = world.CostOf(mode, target);
                Offer(new Label
                {
                    Departure = departure.Name,
                    Position = target,
                    OnFoot = label.OnFoot,
                    Fuel = label.Fuel + fuelCost,
                    Food = label.Food + foodCost,
                    Moves = label.Moves + 1,
                    Parent = label,
                    Action = direction
                });
            }
        }

        return goalLabels;

        void Offer(Label candidate)
        {
            if (candidate.Fuel > fuelCap + Tolerance || candidate.Food > foodCap + Tolerance)
                return;

            var key = (candidate.Position, candidate.OnFoot);
            if (!settled.TryGetValue(key, out var existing))
            {
                existing = [];
                settled[key] = existing;
            }

            if (existing.Any(other => Dominates(other, candidate) || SameCost(other, candidate)))
                return;

            existing.RemoveAll(other => Dominates(candidate, other));
            existing.Add(candidate);

            if (candidate.Position == world.Map.Goal)
            {
                goalLabels.RemoveAll(other => Dominates(candidate, other));
                goalLabels.Add(candidate);
                return;
            }

            frontier.Enqueue(candidate);
        }
    }

    private static bool SameCost(Label left, Label right) =>
        Math.Abs(left.Fuel - right.Fuel) < Tolerance && Math.Abs(left.Food - right.Food) < Tolerance && left.Moves == right.Moves;

    private static bool Dominates(Label left, Label right) =>
        left.Fuel <= right.Fuel + Tolerance
        && left.Food <= right.Food + Tolerance
        && left.Moves <= right.Moves
        && (left.Fuel < right.Fuel - Tolerance || left.Food < right.Food - Tolerance || left.Moves < right.Moves);
}
