using _03_04_zadanie.Catalog;

namespace _03_04_zadanie.Tools;

/// <summary>
/// Answers the question the agent is actually sent to answer: which cities sell the whole list at
/// once. An empty intersection is a result, not a failure, so it comes back with the one hint that
/// can turn it into a hit — check that the parts of the set share their ratings.
/// </summary>
public sealed class CitiesWithAllTool
{
    private const int MaxQuotedQuery = 40;
    private const int MaxQuotedPart = 24;

    private readonly OfferFinder finder;

    public CitiesWithAllTool(OfferFinder finder) => this.finder = finder;

    public string Describe(string? parameters)
    {
        var parts = QuerySplitter.Split(parameters);
        if (parts.Count == 0)
        {
            return ToolOutput.Compose(
                "Pusty parametr. Podaj liste przedmiotow po przecinku,",
                "np. \"turbina wiatrowa 48V, inwerter 48V, akumulator 48V\".");
        }

        var result = finder.FindCommonCities(parts);

        if (result.Unresolved.Count > 0)
        {
            var unknown = string.Join(", ", result.Unresolved.Select(part => $"\"{ToolOutput.Shorten(part, MaxQuotedQuery)}\""));
            return ToolOutput.Compose(
                $"Nie rozpoznano: {unknown}.",
                result.Resolved.Count > 0 ? $"Rozpoznano: {FormatLabels(result)}." : null,
                "Nie licze wspolnych miast dla niepelnej listy - popraw nierozpoznane pozycje i powtorz.");
        }

        if (result.Cities.Count > 0)
        {
            var cities = string.Join(", ", result.Cities.Select(city => city.Name));
            return ToolOutput.Compose(
                $"Miasta majace wszystkie ({result.Resolved.Count}/{result.Resolved.Count}): {cities}",
                $"Dopasowano: {FormatLabels(result)}.",
                AmbiguityWarning(result));
        }

        var counts = string.Join("; ", result.Resolved.Select(part => $"{Label(part)}: {part.Cities.Count}"));
        return ToolOutput.Compose(
            $"Brak miasta z kompletem {result.Resolved.Count} pozycji.",
            $"Miasta na pozycje - {counts}.",
            "Wskazowka: sprawdz, czy pozycje sa wzajemnie zgodne (np. to samo napiecie w calym zestawie) i powtorz.");
    }

    private static string FormatLabels(CommonCityResult result) => string.Join(" | ", result.Resolved.Select(Label));

    /// <summary>
    /// Names the matched item, but only when the phrase left no choice. A part that fits several
    /// variants is reported as the phrase plus the count, because the cities behind it come from
    /// all of them — naming one variant would claim more than was checked.
    /// </summary>
    private static string Label(ResolvedPart part) =>
        part.Match.IsAmbiguous
            ? $"{ToolOutput.Shorten(part.Part, MaxQuotedPart)} ({part.Match.TotalCandidates} warianty)"
            : part.Match.Matches[0].Item.Item.Name;

    private static string? AmbiguityWarning(CommonCityResult result)
    {
        var ambiguous = result.Resolved.Count(part => part.Match.IsAmbiguous);
        return ambiguous == 0
            ? null
            : $"Uwaga: {ambiguous} z {result.Resolved.Count} pozycji pasuje do kilku wariantow - miasto moze oferowac rozne warianty. Podaj parametry, aby to zawezic.";
    }
}
