using System.Text;
using System.Text.Json;

namespace _04_05_zadanie.Warehouse;

public sealed record CityDemand(string City, IReadOnlyDictionary<string, int> Items)
{
    public int TotalQuantity => Items.Values.Sum();
}

/// <summary>
/// What each city needs, read from the hub's demand file. This is the only source of quantities in
/// the whole program: the model never sees a number it could retype, and the executor appends
/// exactly these items, no more and no less.
/// </summary>
public sealed class Demand
{
    private Demand(IReadOnlyList<CityDemand> cities) => Cities = cities;

    public IReadOnlyList<CityDemand> Cities { get; }

    public static Demand Load(string path) => Parse(File.ReadAllText(path));

    public static Demand Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
            throw new FormatException("The demand file must be a JSON object keyed by city.");

        var cities = new List<CityDemand>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var cityProperty in root.EnumerateObject())
        {
            var city = TextNormalizer.Fold(cityProperty.Name);
            if (city.Length == 0)
                throw new FormatException("A city name in the demand file is empty.");
            if (!seen.Add(city))
                throw new FormatException($"City '{cityProperty.Name}' appears twice in the demand file.");
            if (cityProperty.Value.ValueKind != JsonValueKind.Object)
                throw new FormatException($"City '{cityProperty.Name}' must map to an object of item quantities.");

            var items = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var itemProperty in cityProperty.Value.EnumerateObject())
            {
                var item = itemProperty.Name.Trim();
                if (item.Length == 0)
                    throw new FormatException($"City '{cityProperty.Name}' has an item with an empty name.");
                if (itemProperty.Value.ValueKind != JsonValueKind.Number || !itemProperty.Value.TryGetInt32(out var quantity) || quantity <= 0)
                    throw new FormatException($"City '{cityProperty.Name}', item '{item}': the quantity must be a positive integer, got {itemProperty.Value.GetRawText()}.");
                if (!items.TryAdd(item, quantity))
                    throw new FormatException($"City '{cityProperty.Name}' lists item '{item}' twice.");
            }

            if (items.Count == 0)
                throw new FormatException($"City '{cityProperty.Name}' has no items.");

            cities.Add(new CityDemand(city, items));
        }

        if (cities.Count == 0)
            throw new FormatException("The demand file lists no cities.");

        return new Demand(cities);
    }

    public CityDemand? Find(string? city) => Cities.FirstOrDefault(c => TextNormalizer.SameName(c.City, city));

    public string Render()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{Cities.Count} cities, {Cities.Sum(c => c.Items.Count)} order lines:");
        foreach (var city in Cities)
            sb.AppendLine($"  {city.City}: {string.Join(", ", city.Items.Select(i => $"{i.Key} {i.Value}"))}");
        return sb.ToString().TrimEnd();
    }
}
