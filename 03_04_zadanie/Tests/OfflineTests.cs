using System.Text;
using _03_04_zadanie.Catalog;
using _03_04_zadanie.Tools;

namespace _03_04_zadanie.Tests;

/// <summary>
/// The whole tool chain checked without a network, a key or the central: how phrases resolve onto
/// catalog items, what the two tools answer, and that no answer can leave the 4..500 byte window.
/// Each case is a way the agent could phrase its request — or a way the tools could fail it.
/// </summary>
public static class OfflineTests
{
    public static bool Run(string dataDirectory)
    {
        var catalog = ItemCatalog.Load(dataDirectory);
        var finder = new OfferFinder(new ItemMatcher(catalog));
        var offers = new OffersTool(finder);
        var citiesWithAll = new CitiesWithAllTool(finder);
        var results = new List<(string Name, bool Ok, string Detail)>();

        void Check(string name, bool ok, string detail = "") => results.Add((name, ok, detail));

        string Answer(Func<string> produce)
        {
            var output = produce();
            Check($"budzet bajtow: {ToolOutput.Shorten(output, 40)}", WithinBudget(output), $"{ToolOutput.WireLength(output)} B na drucie");
            return output;
        }

        // Tokenization: the agent writes ratings any way it likes.
        Check("jednostka po spacji laczy sie z liczba", Tokens("turbina 48 V").Contains("48v"));
        Check("jednostka bez spacji zostaje jednym tokenem", Tokens("inwerter 1500W").Contains("1500w"));
        Check("rozpisana jednostka zamienia sie na symbol", Tokens("na 48 woltow").Contains("48v"));
        Check("diakrytyki znikaja", Tokens("wyświetlacz").Contains("wyswietlacz"));
        Check("nazwa katalogowa daje oba parametry",
            Tokens("Turbina wiatrowa 400W 48V").Contains("400w") && Tokens("Turbina wiatrowa 400W 48V").Contains("48v"));

        // Matching a single good.
        Check("pelny opis trafia w jeden wariant", Only(finder, "turbina wiatrowa 400W 48V", "Turbina wiatrowa 400W 48V"));
        Check("sam typ zwraca oba warianty turbiny", finder.Lookup("turbina wiatrowa").TotalCandidates == 2);
        Check("odmiana i zdanie opisowe nie przeszkadzaja",
            Only(finder, "potrzebuje turbiny wiatrowej na 48 V", "Turbina wiatrowa 400W 48V"));
        Check("zapytanie po angielsku trafia w ten sam przedmiot",
            Only(finder, "wind turbine 48V", "Turbina wiatrowa 400W 48V"));
        Check("synonim bateria prowadzi do akumulatora",
            Only(finder, "bateria AGM 48V 150Ah", "Akumulator AGM 48V 150Ah"));
        Check("synonim przetwornica prowadzi do inwertera",
            Only(finder, "przetwornica DC/AC 48V 3000W", "Inwerter DC/AC 48V 3000W"));
        Check("sprzeczne napiecie odrzuca drugi wariant",
            Only(finder, "turbina wiatrowa 24V", "Turbina wiatrowa 400W 24V"));
        Check("numer katalogowy nie jest przyblizany",
            finder.Lookup("dioda prostownicza 1N4001").Matches.All(match => !match.Item.Item.Name.Contains("1N4007")));
        Check("zapytanie z innej bajki nie dopasowuje sie", !finder.Lookup("smok wawelski z Krakowa").HasMatches);
        Check("ogolna kategoria daje wiele kandydatow", finder.Lookup("kondensator").TotalCandidates > 4);

        // The single-item tool.
        var turbines = Answer(() => offers.Describe("turbina wiatrowa"));
        Check("oba warianty turbiny widoczne w odpowiedzi",
            turbines.Contains("400W 48V") && turbines.Contains("400W 24V"));
        Check("odpowiedz wskazuje narzedzie od kompletu", turbines.Contains(ToolRoutes.CitiesWithAll));

        var turbine48 = Answer(() => offers.Describe("turbina wiatrowa 48V"));
        Check("miasta turbiny 48V sa kompletne",
            turbine48.Contains("Skolwin") && turbine48.Contains("Domatowo") && turbine48.Contains("Rzeszow"));

        var noOffers = Answer(() => offers.Describe("akumulator kwasowy 12V 200Ah"));
        Check("przedmiot bez sprzedawcy mowi o tym wprost", noOffers.Contains("brak miast"));

        var nonsense = Answer(() => offers.Describe("qwerty zxcvbn"));
        Check("brak dopasowania jest odpowiedzia, nie cisza", nonsense.Contains("Brak dopasowania"));

        Answer(() => offers.Describe(null));
        Answer(() => offers.Describe(""));
        Answer(() => offers.Describe(new string('a', 2000)));
        Answer(() => offers.Describe("dioda " + string.Join(" ", Enumerable.Repeat("prostownicza", 200))));

        // The full-set tool.
        var set48 = Answer(() => citiesWithAll.Describe("turbina wiatrowa 48V, inwerter DC/AC 48V 3000W, akumulator AGM 48V 150Ah"));
        Check("spojny zestaw 48V wskazuje dwa miasta",
            set48.Contains("Domatowo") && set48.Contains("Skolwin") && !set48.Contains("Rzeszow"));

        var sentence = Answer(() => citiesWithAll.Describe(
            "potrzebuje turbiny wiatrowej 400W 48V oraz inwertera DC/AC 48V 3000W i akumulatora AGM 48V 150Ah"));
        Check("zdanie z 'oraz' i 'i' dzieli sie na trzy pozycje",
            sentence.Contains("Domatowo") && sentence.Contains("Skolwin"));

        var generic = Answer(() => citiesWithAll.Describe("turbina wiatrowa, inwerter, akumulator"));
        Check("zestaw bez parametrow tez wskazuje dwa miasta",
            generic.Contains("Domatowo") && generic.Contains("Skolwin"));
        Check("wieloznacznosc jest zglaszana", generic.Contains("Uwaga"));

        var mixed = Answer(() => citiesWithAll.Describe("turbina wiatrowa 24V, inwerter DC/AC 12V 1500W, akumulator AGM 48V 150Ah"));
        Check("niespojny zestaw nie daje miasta", mixed.Contains("Brak miasta z kompletem"));
        Check("pusty wynik niesie wskazowke naprawcza", mixed.Contains("napiecie"));

        var partial = Answer(() => citiesWithAll.Describe("turbina wiatrowa 48V, qwerty zxcvbn"));
        Check("nierozpoznana pozycja wstrzymuje wynik",
            partial.Contains("Nie rozpoznano") && !partial.Contains("Domatowo"));

        Answer(() => citiesWithAll.Describe(null));
        Answer(() => citiesWithAll.Describe("   "));
        Answer(() => citiesWithAll.Describe(string.Join(", ", Enumerable.Repeat("turbina wiatrowa 48V", 40))));

        // Registration. The central caps a description at 300 characters and says so only by
        // rejecting the whole submission, so the limit is checked here and before every send.
        Check("opis narzedzia od pojedynczego przedmiotu miesci sie w limicie",
            ToolCatalog.OffersDescription.Length <= ToolCatalog.MaxDescriptionLength,
            $"{ToolCatalog.OffersDescription.Length} znakow");
        Check("opis narzedzia od kompletu miesci sie w limicie",
            ToolCatalog.CitiesWithAllDescription.Length <= ToolCatalog.MaxDescriptionLength,
            $"{ToolCatalog.CitiesWithAllDescription.Length} znakow");
        Check("oba opisy mowia wprost, co wlozyc w params",
            ToolCatalog.OffersDescription.Contains("params") && ToolCatalog.CitiesWithAllDescription.Contains("params"));
        Check("oba opisy niosa przyklad zapytania",
            ToolCatalog.OffersDescription.Contains("48V") && ToolCatalog.CitiesWithAllDescription.Contains("48V"));
        Check("opis pojedynczego przedmiotu kieruje do narzedzia od kompletu",
            ToolCatalog.OffersDescription.Contains("cities-with-all"));
        Check("zgloszenie przechodzi wlasna kontrole przed wyslaniem",
            ToolCatalog.Validate("https://przyklad.test").Count == 0);

        return Report(results);
    }

    private static IReadOnlyList<string> Tokens(string text) =>
        Synonyms.Expand(TextNormalizer.Tokenize(text, dropStopWords: true));

    private static bool Only(OfferFinder finder, string query, string expectedName)
    {
        var match = finder.Lookup(query);
        return match.TotalCandidates == 1 && match.Matches[0].Item.Item.Name == expectedName;
    }

    private static bool WithinBudget(string output) =>
        output.Length >= ToolOutput.MinBytes
        && ToolOutput.WireLength(output) <= ToolOutput.MaxBytes
        && Encoding.UTF8.GetByteCount(output) == output.Length;

    private static bool Report(List<(string Name, bool Ok, string Detail)> results)
    {
        foreach (var (name, ok, detail) in results)
        {
            var suffix = string.IsNullOrEmpty(detail) ? string.Empty : $"  ({detail})";
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{suffix}");
        }

        var failed = results.Count(result => !result.Ok);
        Console.WriteLine();
        Console.WriteLine($"{results.Count - failed}/{results.Count} testow przeszlo");
        return failed == 0;
    }
}
