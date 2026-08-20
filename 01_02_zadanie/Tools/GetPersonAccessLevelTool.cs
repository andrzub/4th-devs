using System.Text;
using System.Text.Json;

namespace _01_02_zadanie.Tools;

internal class GetPersonAccessLevelTool(string apiKey) : ITool
{
    private const string AccessLevelApiEndpoint = "https://hub.ag3nts.org/api/accesslevel";

    public string Name => "get_person_access_level";

    public string Description => "Retrieves the numeric access level of a person based on their Name, Surname and Birthyear.";

    public JsonElement ParametersSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "Name": { "type": "string", "description": "Name of the person." },
            "Surname": { "type": "string", "description": "Surname of the person." },
            "Birthyear": { "type": "number", "description": "Birthyear of the person." }
          },
          "required": ["Name", "Surname", "Birthyear"]
        }
        """);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        string name = null;
        string surname = null;
        int? birthyear = null;

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
            if (args.TryGetProperty("Birthyear", out var birthyearProp))
            {
                birthyear = birthyearProp.GetInt32();
            }
        }
        catch
        {
            // ignore malformed arguments
        }

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(surname) || birthyear is null)
        {
            return "Invalid input. Please provide valid Name, Surname and Birthyear.";
        }

        using var httpClient = new HttpClient();
        var content = new StringContent(JsonSerializer.Serialize(new { apikey = apiKey, name = name, surname = surname, birthYear = birthyear }), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(AccessLevelApiEndpoint, content, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorMessage = await response.Content.ReadAsStringAsync(cancellationToken);
            return $"Failed to retrieve access level. Status code: {response.StatusCode}. Error message: {errorMessage}";
        }

        var accessLevelJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var accessLevel = JsonSerializer.Deserialize<JsonElement>(accessLevelJson);

        var accessLevelValue = accessLevel.GetProperty("accessLevel").GetInt32();

        return $"access level: {accessLevelValue}";
    }
}
