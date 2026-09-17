using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using _03_04_zadanie.Tools;

namespace _03_04_zadanie.Api;

/// <summary>
/// The public face of both tools. The agent stops working the moment a call returns nothing, so
/// every path through here ends in HTTP 200 with an output field: a malformed body, a missing
/// parameter or an exception are all answered with something the agent can read and act on.
/// </summary>
public static class NegotiationsApi
{
    private static readonly JsonSerializerOptions ResponseOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void MapNegotiationTools(this WebApplication app, OffersTool offers, CitiesWithAllTool citiesWithAll, RequestLog log)
    {
        app.MapPost(ToolRoutes.Offers, (HttpRequest request) => Handle(request, log, ToolRoutes.Offers, offers.Describe));
        app.MapPost(ToolRoutes.CitiesWithAll, (HttpRequest request) => Handle(request, log, ToolRoutes.CitiesWithAll, citiesWithAll.Describe));

        app.MapGet("/", () => Results.Text(
            $"negotiations tools: POST {ToolRoutes.Offers}, POST {ToolRoutes.CitiesWithAll} with body {{\"params\": \"...\"}}",
            "text/plain"));
    }

    private static async Task<IResult> Handle(HttpRequest request, RequestLog log, string route, Func<string?, string> describe)
    {
        string? parameters = null;
        string body;

        using (var reader = new StreamReader(request.Body))
        {
            body = await reader.ReadToEndAsync();
        }

        string output;
        try
        {
            parameters = ReadParameters(body);
            output = describe(parameters);
        }
        catch (Exception exception)
        {
            output = ToolOutput.Compose(
                "Blad narzedzia przy przetwarzaniu zapytania.",
                "Powtorz z prostszym opisem przedmiotu, np. \"turbina wiatrowa 48V\".");
            log.Write(route, body, parameters, output, exception.Message);
            return Results.Json(new ToolResponse(output), ResponseOptions);
        }

        log.Write(route, body, parameters, output, error: null);
        return Results.Json(new ToolResponse(output), ResponseOptions);
    }

    /// <summary>
    /// The contract says the parameter arrives as {"params": "..."}, but a tool that answers only
    /// perfectly shaped calls is a tool that goes silent on the first surprise. Plain text, a
    /// non-string params value and the usual alternative field names are all accepted.
    /// </summary>
    private static string? ReadParameters(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                return document.RootElement.GetString();
            }

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return document.RootElement.ToString();
            }

            foreach (var name in new[] { "params", "param", "query", "q", "input", "text", "message" })
            {
                if (!document.RootElement.TryGetProperty(name, out var value))
                {
                    continue;
                }

                return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
            }

            return document.RootElement.ToString();
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private sealed record ToolResponse([property: JsonPropertyName("output")] string Output);
}
