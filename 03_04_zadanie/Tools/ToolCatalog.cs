using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace _03_04_zadanie.Tools;

public sealed record ToolRegistration(
    [property: JsonPropertyName("URL")] string Url,
    [property: JsonPropertyName("description")] string Description);

/// <summary>
/// What the central's agent is told about the two endpoints. This text is the only prompt that
/// agent ever gets about them, and the central caps it at 300 characters — a budget that buys
/// roughly three sentences each: what the tool answers, what to put in params, and the one warning
/// that decides whether the whole set can be bought in one place.
/// </summary>
public static class ToolCatalog
{
    /// <summary>
    /// Undocumented limit, learned from rejection -875: "Field description can contain a maximum of
    /// 300 characters". Checked before sending, so an over-long text costs nothing.
    /// </summary>
    public const int MaxDescriptionLength = 300;

    private static readonly JsonSerializerOptions PayloadOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public const string OffersDescription =
        "Miasta, w ktorych kupisz JEDEN przedmiot (elektronika i sprzet zasilajacy: turbiny wiatrowe, "
        + "inwertery, akumulatory). W params podaj nazwe przedmiotu z parametrami, np. 'turbina wiatrowa "
        + "400W 48V'. Zwraca pasujace warianty i ich miasta. Kilka przedmiotow naraz: uzyj cities-with-all.";

    public const string CitiesWithAllDescription =
        "Miasta oferujace JEDNOCZESNIE wszystkie przedmioty z listy - jedno zapytanie zamiast sprawdzania "
        + "po kolei. W params podaj liste po przecinku, z parametrami, np. 'turbina wiatrowa 400W 48V, "
        + "inwerter 48V 3000W, akumulator AGM 48V'. Pozycje o roznych napieciach moga nie miec wspolnego miasta.";

    public static IReadOnlyList<ToolRegistration> Build(string baseUrl)
    {
        var normalized = baseUrl.TrimEnd('/');

        return
        [
            new ToolRegistration(normalized + ToolRoutes.Offers, OffersDescription),
            new ToolRegistration(normalized + ToolRoutes.CitiesWithAll, CitiesWithAllDescription),
        ];
    }

    public static IReadOnlyList<string> Validate(string baseUrl) =>
        Build(baseUrl)
            .Where(tool => tool.Description.Length > MaxDescriptionLength)
            .Select(tool => $"{tool.Url}: opis ma {tool.Description.Length} znakow, limit to {MaxDescriptionLength}")
            .ToList();

    public static string BuildSubmissionPayload(string apiKey, string taskName, string baseUrl)
    {
        var payload = new
        {
            apikey = apiKey,
            task = taskName,
            answer = new { tools = Build(baseUrl) },
        };

        return JsonSerializer.Serialize(payload, PayloadOptions);
    }

    public static string BuildCheckPayload(string apiKey, string taskName)
    {
        var payload = new
        {
            apikey = apiKey,
            task = taskName,
            answer = new { action = "check" },
        };

        return JsonSerializer.Serialize(payload, PayloadOptions);
    }
}
