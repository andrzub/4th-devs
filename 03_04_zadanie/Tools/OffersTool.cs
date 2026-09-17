using _03_04_zadanie.Catalog;

namespace _03_04_zadanie.Tools;

/// <summary>
/// Answers "who sells this?" for a single good. When the phrase fits several variants they are all
/// listed with their own cities, because the difference between them (48 V against 24 V) is exactly
/// what decides whether a set can be bought in one place — a choice that belongs to the agent.
/// </summary>
public sealed class OffersTool
{
    private const int MaxQuotedQuery = 60;

    private readonly OfferFinder finder;

    public OffersTool(OfferFinder finder) => this.finder = finder;

    public string Describe(string? parameters)
    {
        if (string.IsNullOrWhiteSpace(parameters))
        {
            return ToolOutput.Compose(
                "Pusty parametr. Podaj nazwe przedmiotu z parametrami, np. \"turbina wiatrowa 48V\".");
        }

        var match = finder.Lookup(parameters);
        if (!match.HasMatches)
        {
            return ToolOutput.Compose(
                $"Brak dopasowania dla: \"{ToolOutput.Shorten(parameters, MaxQuotedQuery)}\".",
                "Podaj sama nazwe przedmiotu z parametrami, np. \"akumulator AGM 48V\".",
                "Baza zawiera podzespoly elektroniczne i sprzet zasilajacy.");
        }

        var lines = new List<string?>();
        foreach (var candidate in match.Matches)
        {
            lines.Add(FormatOffer(candidate.Item));
        }

        if (match.TotalCandidates > match.Matches.Count)
        {
            lines.Add($"Dopasowan: {match.TotalCandidates}. Pokazano {match.Matches.Count} - doprecyzuj typ, moc lub napiecie.");
        }

        lines.Add($"Miasta majace kilka przedmiotow naraz: {ToolRoutes.CitiesWithAll} z cala lista.");
        return ToolOutput.Compose(lines);
    }

    private static string FormatOffer(IndexedItem item) =>
        item.Cities.Count == 0
            ? $"{item.Item.Name}: brak miast oferujacych ten przedmiot"
            : $"{item.Item.Name}: {string.Join(", ", item.Cities.Select(city => city.Name))}";
}
