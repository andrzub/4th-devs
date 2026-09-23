namespace _04_03_zadanie.City;

/// <summary>
/// Shortest routes on the board. The hub computes the real path when a unit moves; these are the
/// projections the guard prices an action with before it is sent, so they mirror the two rules help
/// states: transporters stay on streets, scouts take the shortest orthogonal route over anything.
/// </summary>
public static class Pathfinder
{
    /// <summary>Street-only route for a transporter, start excluded, or null when no street connects the two fields.</summary>
    public static IReadOnlyList<Coordinate>? RoadPath(CityMap map, Coordinate from, Coordinate to)
    {
        if (!map.IsRoad(from) || !map.IsRoad(to))
            return null;

        return ShortestPath(from, to, map.IsRoad);
    }

    /// <summary>A scout walks over anything, so the route is as long as the Manhattan distance; the fields are listed for the record.</summary>
    public static IReadOnlyList<Coordinate> ScoutPath(Coordinate from, Coordinate to) => ShortestPath(from, to, _ => true)!;

    private static IReadOnlyList<Coordinate>? ShortestPath(Coordinate from, Coordinate to, Func<Coordinate, bool> passable)
    {
        if (from == to)
            return [];

        var previous = new Dictionary<Coordinate, Coordinate> { [from] = from };
        var queue = new Queue<Coordinate>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var next in current.Neighbours())
            {
                if (previous.ContainsKey(next) || !passable(next))
                    continue;

                previous[next] = current;
                if (next == to)
                    return Unwind(previous, from, to);

                queue.Enqueue(next);
            }
        }

        return null;
    }

    private static List<Coordinate> Unwind(Dictionary<Coordinate, Coordinate> previous, Coordinate from, Coordinate to)
    {
        var path = new List<Coordinate>();
        for (var step = to; step != from; step = previous[step])
            path.Add(step);

        path.Reverse();
        return path;
    }
}
