using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace _01_02_zadanie.Tools;

internal class GetPersonLocationsTool(string apiKey) : ITool
{
    private const string LocationsApiEndpoint = "https://hub.ag3nts.org/api/location";

    public string Name => "get_person_locations";

    public string Description => "Retrieves the geographical locations (list of latitude and longitude) of a person based on their Name and Surname.";

    public JsonElement ParametersSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "Name": { "type": "string", "description": "Name of the person." },
            "Surname": { "type": "string", "description": "Surname of the person." }
          },
          "required": ["Name", "Surname"]
        }
        """);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        string name = null;
        string surname = null;

        try
        {
            var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
            if (args.TryGetProperty("Name", out var nameProp))
            {
                name = nameProp.GetString();
            }
            if (args.TryGetProperty("Surname", out var surnameProp))
            {
                surname = surnameProp.GetString();
            }
        }
        catch
        {
            // ignore malformed arguments
        }

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(surname))
        {
            return "Invalid input. Please provide valid Name and Surname.";
        }

        using var httpClient = new HttpClient();
        var content = new StringContent(JsonSerializer.Serialize(new { apikey = apiKey, name = name, surname = surname }), Encoding.UTF8, "application/json");

        var response = await httpClient.PostAsync(LocationsApiEndpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return $"Failed to retrieve locations. Status code: {response.StatusCode}";
        }

        var locationsJson = await response.Content.ReadAsStringAsync(cancellationToken);
        return locationsJson;
    }
}