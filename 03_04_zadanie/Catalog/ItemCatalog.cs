namespace _03_04_zadanie.Catalog;

public sealed record City(string Name, string Code);

public sealed record CatalogItem(string Name, string Code);

/// <summary>
/// One catalog item with everything matching needs precomputed: its tokens, the measurements read
/// out of its name (48 V and 24 V are the whole difference between two otherwise identical items)
/// and the cities that sell it.
/// </summary>
public sealed class IndexedItem
{
    public required CatalogItem Item { get; init; }

    public required IReadOnlyList<string> Tokens { get; init; }

    public required IReadOnlyDictionary<string, HashSet<double>> Measurements { get; init; }

    public required IReadOnlyList<City> Cities { get; init; }
}

public sealed class ItemCatalog
{
    private ItemCatalog(IReadOnlyList<IndexedItem> items, IReadOnlyList<City> cities)
    {
        Items = items;
        Cities = cities;
    }

    public IReadOnlyList<IndexedItem> Items { get; }

    public IReadOnlyList<City> Cities { get; }

    public static ItemCatalog Load(string dataDirectory)
    {
        var cities = ReadPairs(Path.Combine(dataDirectory, "cities.csv"))
            .Select(pair => new City(pair.First, pair.Second))
            .ToList();

        var citiesByCode = cities.ToDictionary(city => city.Code, StringComparer.OrdinalIgnoreCase);

        var offersByItemCode = new Dictionary<string, List<City>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (itemCode, cityCode) in ReadPairs(Path.Combine(dataDirectory, "connections.csv")))
        {
            if (!citiesByCode.TryGetValue(cityCode, out var city))
            {
                continue;
            }

            if (!offersByItemCode.TryGetValue(itemCode, out var list))
            {
                list = [];
                offersByItemCode[itemCode] = list;
            }

            list.Add(city);
        }

        var items = ReadPairs(Path.Combine(dataDirectory, "items.csv"))
            .Select(pair => Index(new CatalogItem(pair.First, pair.Second), offersByItemCode))
            .ToList();

        return new ItemCatalog(items, cities);
    }

    private static IndexedItem Index(CatalogItem item, Dictionary<string, List<City>> offersByItemCode)
    {
        var tokens = TextNormalizer.Tokenize(item.Name, dropStopWords: false);
        var measurements = new Dictionary<string, HashSet<double>>(StringComparer.Ordinal);

        foreach (var token in tokens)
        {
            if (!TextNormalizer.TryParseMeasurement(token, out var unit, out var value))
            {
                continue;
            }

            if (!measurements.TryGetValue(unit, out var values))
            {
                values = [];
                measurements[unit] = values;
            }

            values.Add(value);
        }

        var cities = offersByItemCode.TryGetValue(item.Code, out var offers)
            ? offers.OrderBy(city => city.Name, StringComparer.Ordinal).ToList()
            : [];

        return new IndexedItem
        {
            Item = item,
            Tokens = tokens,
            Measurements = measurements,
            Cities = cities,
        };
    }

    private static IEnumerable<(string First, string Second)> ReadPairs(string path)
    {
        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            // Item names are free text, so the code is whatever follows the LAST separator.
            var separator = line.LastIndexOf(',');
            if (separator <= 0)
            {
                continue;
            }

            yield return (line[..separator].Trim(), line[(separator + 1)..].Trim());
        }
    }
}
