using System.Text;

namespace _03_05_zadanie.World;

public sealed record RouteOutcome(bool ReachedGoal, string? Failure, double FuelUsed, double FoodUsed, int Moves, Position Final, string Report)
{
    public bool IsValid => ReachedGoal && Failure is null;
}

/// <summary>
/// Replays an instruction list against the registered world. This is the guard in front of every
/// submission: a route that dies here costs one turn of the agent and no request at all, while the
/// same route sent to the hub costs one of the few attempts the run has.
/// </summary>
public static class RouteSimulator
{
    public static RouteOutcome Run(WorldModel world, IReadOnlyList<string> instructions)
    {
        var log = new StringBuilder();

        if (instructions.Count == 0)
            return Failed(world.Map.Start, 0, 0, 0, "The instruction list is empty.", log);

        var mode = world.Find(instructions[0]);
        if (mode is null)
            return Failed(world.Map.Start, 0, 0, 0, $"The first entry must name the departure mode; {instructions[0]} is not a known one.", log);

        if (!mode.SelectableAtStart && !string.Equals(mode.Name, world.WalkMode, StringComparison.OrdinalIgnoreCase))
            return Failed(world.Map.Start, 0, 0, 0, $"{mode.Name} cannot be chosen at departure.", log);

        var position = world.Map.Start;
        var fuel = world.FuelBudget;
        var food = world.FoodBudget;
        var moves = 0;
        var onFoot = string.Equals(mode.Name, world.WalkMode, StringComparison.OrdinalIgnoreCase);

        log.AppendLine($"depart {position} in {mode.Name} with fuel {WorldModel.Format(fuel)}, food {WorldModel.Format(food)}");

        for (var index = 1; index < instructions.Count; index++)
        {
            var token = instructions[index];

            if (position == world.Map.Goal)
                return Failed(position, moves, world.FuelBudget - fuel, world.FoodBudget - food,
                    $"The goal is already reached at step {index - 1}, but the list continues with {token}.", log);

            if (RouteInstructions.IsDismount(token))
            {
                if (!world.DismountAllowed)
                    return Failed(position, moves, world.FuelBudget - fuel, world.FoodBudget - food, "Dismounting is not allowed in this world.", log);

                if (onFoot)
                    return Failed(position, moves, world.FuelBudget - fuel, world.FoodBudget - food, $"Step {index}: dismount while already travelling on foot.", log);

                onFoot = true;
                mode = world.Walk;
                log.AppendLine($"step {index,2}  dismount at {position}, continuing as {mode.Name}");
                continue;
            }

            if (!RouteInstructions.TryOffset(token, out var offset))
                return Failed(position, moves, world.FuelBudget - fuel, world.FoodBudget - food,
                    $"Step {index}: {token} is not a move. Valid: {string.Join(", ", RouteInstructions.Directions)}, {RouteInstructions.Dismount}.", log);

            var target = RouteInstructions.Apply(position, offset);
            if (!world.CanEnter(mode, target, out var refusal))
                return Failed(position, moves, world.FuelBudget - fuel, world.FoodBudget - food, $"Step {index} ({token}): {refusal}", log);

            var (fuelCost, foodCost) = world.CostOf(mode, target);
            fuel -= fuelCost;
            food -= foodCost;
            moves++;
            position = target;

            log.AppendLine($"step {index,2}  {token,-8} {mode.Name,-6} -> {position} {world.Map.At(position)}  fuel {WorldModel.Format(fuel),5}  food {WorldModel.Format(food),5}");

            if (fuel < -Tolerance)
                return Failed(position, moves, world.FuelBudget - fuel, world.FoodBudget - food, $"Step {index}: out of fuel.", log);

            if (food < -Tolerance)
                return Failed(position, moves, world.FuelBudget - fuel, world.FoodBudget - food, $"Step {index}: out of food.", log);
        }

        var fuelUsed = world.FuelBudget - fuel;
        var foodUsed = world.FoodBudget - food;

        if (position != world.Map.Goal)
            return Failed(position, moves, fuelUsed, foodUsed, $"The route ends at {position} instead of the goal {world.Map.Goal}.", log);

        log.AppendLine($"goal reached in {moves} moves, fuel used {WorldModel.Format(fuelUsed)}/{WorldModel.Format(world.FuelBudget)}, food used {WorldModel.Format(foodUsed)}/{WorldModel.Format(world.FoodBudget)}");
        return new RouteOutcome(true, null, fuelUsed, foodUsed, moves, position, log.ToString().TrimEnd());
    }

    private const double Tolerance = 1e-9;

    private static RouteOutcome Failed(Position position, int moves, double fuelUsed, double foodUsed, string failure, StringBuilder log)
    {
        log.AppendLine(failure);
        return new RouteOutcome(false, failure, fuelUsed, foodUsed, moves, position, log.ToString().TrimEnd());
    }
}
