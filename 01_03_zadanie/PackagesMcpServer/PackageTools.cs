using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace PackagesMcpServer;

/// <summary>
/// MCP tools for the railway shipment system. Both tools wrap the raw API response in a
/// small envelope with a hint, so the calling agent knows what to do with the result.
/// </summary>
[McpServerToolType]
public sealed class PackageTools(PackagesApiClient api)
{
    [McpServerTool(Name = "check_package", ReadOnly = true, OpenWorld = true)]
    [Description("Sprawdza aktualny status i lokalizację przesyłki kolejowej o podanym identyfikatorze. Zwraca dane przesyłki wraz z jej zawartością i obecnym miejscem docelowym.")]
    public async Task<string> CheckPackageAsync(
        [Description("Identyfikator przesyłki, np. PKG12345678.")] string packageId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return Envelope(null, "Nie podano identyfikatora przesyłki. Poproś operatora o numer paczki w formacie PKG########.");

        var raw = await api.CheckAsync(packageId.Trim(), cancellationToken);

        return Envelope(raw, "Status przesyłki odczytany. Aby zmienić miejsce docelowe, potrzebny jest kod zabezpieczający od operatora — wywołaj wtedy redirect_package.");
    }

    [McpServerTool(Name = "redirect_package", Destructive = true, OpenWorld = true)]
    [Description("Przekierowuje przesyłkę kolejową do wskazanej lokalizacji docelowej. Wymaga kodu zabezpieczającego, który podaje operator systemu. Zwraca potwierdzenie przekierowania.")]
    public async Task<string> RedirectPackageAsync(
        [Description("Identyfikator przesyłki, np. PKG12345678.")] string packageId,
        [Description("Kod lokalizacji docelowej, np. PWR3847PL.")] string destination,
        [Description("Kod zabezpieczający podany przez operatora — bez niego system odrzuci zmianę.")] string code,
        CancellationToken cancellationToken = default)
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(packageId)) missing.Add("packageId");
        if (string.IsNullOrWhiteSpace(destination)) missing.Add("destination");
        if (string.IsNullOrWhiteSpace(code)) missing.Add("code");

        if (missing.Count > 0)
            return Envelope(null, $"Brak wymaganych danych: {string.Join(", ", missing)}. Kod zabezpieczający musi podać operator — dopytaj go, jeśli go nie przekazał.");

        var raw = await api.RedirectAsync(packageId.Trim(), destination.Trim(), code.Trim(), cancellationToken);

        return Envelope(raw, "Jeśli odpowiedź zawiera pole 'confirmation', przekaż jego wartość operatorowi — to jego potwierdzenie zmiany. Jeśli kod zabezpieczający został odrzucony, poproś operatora o poprawny kod.");
    }

    private static string Envelope(string? rawApiResponse, string hint)
    {
        var envelope = new JsonObject { ["hint"] = hint };

        if (rawApiResponse is not null)
        {
            // Keep the API payload as structured JSON when possible so the model does not read escaped text.
            envelope["result"] = TryParse(rawApiResponse) ?? JsonValue.Create(rawApiResponse);
        }

        return envelope.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static JsonNode? TryParse(string json)
    {
        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
