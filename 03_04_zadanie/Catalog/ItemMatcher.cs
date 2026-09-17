namespace _03_04_zadanie.Catalog;

public sealed record ItemMatch(IndexedItem Item, double Score);

public sealed record MatchResult(string Query, IReadOnlyList<ItemMatch> Matches, int TotalCandidates)
{
    public bool HasMatches => Matches.Count > 0;

    public bool IsAmbiguous => TotalCandidates > 1;
}

/// <summary>
/// Resolves a phrase written by the agent onto catalog items. Rare words carry the meaning
/// (wiatrowa appears twice among 2137 names, dioda three hundred times), so matching is weighted by
/// inverse document frequency; anything containing a digit has to match exactly, because 48 V and
/// 24 V — or 1N4001 and 1N4007 — are different goods, not near misses.
/// </summary>
public sealed class ItemMatcher
{
    private const double MinimumScore = 0.34;
    private const double VariantBand = 0.92;
    private const double PrefixMatchFactor = 0.9;
    private const double ConflictingMeasurementFactor = 0.35;

    private readonly ItemCatalog catalog;
    private readonly Dictionary<string, double> inverseDocumentFrequency;
    private readonly double unknownTokenWeight;

    public ItemMatcher(ItemCatalog catalog)
    {
        this.catalog = catalog;

        var documentFrequency = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in catalog.Items)
        {
            foreach (var token in item.Tokens.Distinct(StringComparer.Ordinal))
            {
                documentFrequency[token] = documentFrequency.GetValueOrDefault(token) + 1;
            }
        }

        var total = catalog.Items.Count;
        inverseDocumentFrequency = documentFrequency.ToDictionary(
            entry => entry.Key,
            entry => Math.Log(1.0 + (double)total / (1 + entry.Value)),
            StringComparer.Ordinal);

        unknownTokenWeight = Math.Log(1.0 + total);
    }

    public int MaxVariants { get; init; } = 4;

    public MatchResult Match(string? query)
    {
        var text = query ?? string.Empty;
        var tokens = Synonyms.Expand(TextNormalizer.Tokenize(text, dropStopWords: true));
        if (tokens.Count == 0)
        {
            return new MatchResult(text, [], 0);
        }

        var queryMeasurements = ReadMeasurements(tokens);
        var totalWeight = tokens.Sum(Weight);

        var scored = new List<ItemMatch>();
        foreach (var item in catalog.Items)
        {
            var score = Score(tokens, queryMeasurements, item, totalWeight);
            if (score >= MinimumScore)
            {
                scored.Add(new ItemMatch(item, score));
            }
        }

        if (scored.Count == 0)
        {
            return new MatchResult(text, [], 0);
        }

        var best = scored.Max(match => match.Score);
        var accepted = scored
            .Where(match => match.Score >= best * VariantBand)
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Item.Item.Name, StringComparer.Ordinal)
            .ToList();

        return new MatchResult(text, accepted.Take(MaxVariants).ToList(), accepted.Count);
    }

    private double Score(
        IReadOnlyList<string> queryTokens,
        IReadOnlyDictionary<string, double> queryMeasurements,
        IndexedItem item,
        double totalWeight)
    {
        var matched = 0.0;

        foreach (var queryToken in queryTokens)
        {
            var bestFactor = 0.0;

            foreach (var itemToken in item.Tokens)
            {
                if (string.Equals(queryToken, itemToken, StringComparison.Ordinal))
                {
                    bestFactor = 1.0;
                    break;
                }

                if (IsPrefixMatch(queryToken, itemToken))
                {
                    bestFactor = Math.Max(bestFactor, PrefixMatchFactor);
                }
            }

            matched += Weight(queryToken) * bestFactor;
        }

        var score = matched / totalWeight;
        return HasConflictingMeasurement(queryMeasurements, item) ? score * ConflictingMeasurementFactor : score;
    }

    private static bool HasConflictingMeasurement(
        IReadOnlyDictionary<string, double> queryMeasurements,
        IndexedItem item)
    {
        foreach (var (unit, value) in queryMeasurements)
        {
            if (item.Measurements.TryGetValue(unit, out var values) && !values.Contains(value))
            {
                return true;
            }
        }

        return false;
    }

    private static Dictionary<string, double> ReadMeasurements(IReadOnlyList<string> tokens)
    {
        var measurements = new Dictionary<string, double>(StringComparer.Ordinal);

        foreach (var token in tokens)
        {
            if (TextNormalizer.TryParseMeasurement(token, out var unit, out var value))
            {
                measurements.TryAdd(unit, value);
            }
        }

        return measurements;
    }

    private static bool IsPrefixMatch(string left, string right)
    {
        var shorter = Math.Min(left.Length, right.Length);

        if (left.Any(char.IsAsciiDigit) || right.Any(char.IsAsciiDigit))
        {
            // Ratings and part numbers differ in their tail, so only a whole-token prefix counts.
            return shorter >= 5
                && (left.StartsWith(right, StringComparison.Ordinal) || right.StartsWith(left, StringComparison.Ordinal));
        }

        var common = CommonPrefixLength(left, right);
        return common >= 4 && common >= shorter - 3;
    }

    private static int CommonPrefixLength(string left, string right)
    {
        var length = 0;
        while (length < left.Length && length < right.Length && left[length] == right[length])
        {
            length++;
        }

        return length;
    }

    private double Weight(string token) => inverseDocumentFrequency.GetValueOrDefault(token, unknownTokenWeight);
}
