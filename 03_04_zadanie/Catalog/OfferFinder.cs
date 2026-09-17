namespace _03_04_zadanie.Catalog;

public sealed record ResolvedPart(string Part, MatchResult Match, IReadOnlyList<City> Cities);

public sealed record CommonCityResult(
    IReadOnlyList<ResolvedPart> Resolved,
    IReadOnlyList<string> Unresolved,
    IReadOnlyList<City> Cities);

/// <summary>
/// The two questions the agent actually has: which cities sell this one thing, and which cities
/// sell every thing on the list at once. A part that resolves to several variants contributes the
/// union of their cities — the agent asked for "a turbine", not for one specific rating, and the
/// warning about the ambiguity is left to the tool that formats the answer.
/// </summary>
public sealed class OfferFinder
{
    private readonly ItemMatcher matcher;

    public OfferFinder(ItemMatcher matcher) => this.matcher = matcher;

    public MatchResult Lookup(string? query) => matcher.Match(query);

    public CommonCityResult FindCommonCities(IReadOnlyList<string> parts)
    {
        var resolved = new List<ResolvedPart>();
        var unresolved = new List<string>();

        foreach (var part in parts)
        {
            var match = matcher.Match(part);
            if (!match.HasMatches)
            {
                unresolved.Add(part);
                continue;
            }

            resolved.Add(new ResolvedPart(part, match, UnionOfCities(match)));
        }

        return new CommonCityResult(resolved, unresolved, Intersect(resolved));
    }

    private static IReadOnlyList<City> UnionOfCities(MatchResult match) =>
        match.Matches
            .SelectMany(candidate => candidate.Item.Cities)
            .DistinctBy(city => city.Code, StringComparer.OrdinalIgnoreCase)
            .OrderBy(city => city.Name, StringComparer.Ordinal)
            .ToList();

    private static IReadOnlyList<City> Intersect(IReadOnlyList<ResolvedPart> resolved)
    {
        if (resolved.Count == 0)
        {
            return [];
        }

        IEnumerable<City> common = resolved[0].Cities;
        foreach (var part in resolved.Skip(1))
        {
            var codes = part.Cities.Select(city => city.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
            common = common.Where(city => codes.Contains(city.Code));
        }

        return common.OrderBy(city => city.Name, StringComparer.Ordinal).ToList();
    }
}
