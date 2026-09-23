namespace _04_03_zadanie.City;

/// <summary>
/// A group of touching fields with the same symbol, together with the streets a transporter can
/// park on right next to it. One scout stepping off at a stop can cover the whole group on foot.
/// </summary>
public sealed record Cluster(string Name, IReadOnlyList<Coordinate> Fields, IReadOnlyList<Coordinate> Stops)
{
    public bool Contains(Coordinate field) => Fields.Contains(field);

    /// <summary>Cluster fields a scout can land on straight from a transporter parked at the stop.</summary>
    public IReadOnlyList<Coordinate> FieldsTouching(Coordinate stop) => Fields.Where(field => field.DistanceTo(stop) == 1).ToList();

    public override string ToString() => Name;
}

public static class ClusterFinder
{
    /// <summary>Groups the fields carrying the symbol into 4-connected components, in reading order of their first field.</summary>
    public static IReadOnlyList<Cluster> Find(CityMap map, string symbol)
    {
        var remaining = new HashSet<Coordinate>(map.FieldsWithSymbol(symbol));
        var clusters = new List<Cluster>();

        while (remaining.Count > 0)
        {
            var seed = remaining.Min();
            var fields = new List<Coordinate>();
            var pending = new Stack<Coordinate>();
            pending.Push(seed);
            remaining.Remove(seed);

            while (pending.Count > 0)
            {
                var field = pending.Pop();
                fields.Add(field);
                foreach (var neighbour in field.Neighbours())
                    if (remaining.Remove(neighbour))
                        pending.Push(neighbour);
            }

            fields.Sort();
            var stops = fields.SelectMany(field => field.Neighbours())
                .Distinct()
                .Where(neighbour => !fields.Contains(neighbour) && map.IsRoad(neighbour))
                .Order()
                .ToList();

            clusters.Add(new Cluster($"{symbol.ToUpperInvariant()} {fields[0]}-{fields[^1]}", fields, stops));
        }

        return clusters.OrderBy(cluster => cluster.Fields[0]).ToList();
    }
}
