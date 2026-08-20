using System.Text;
using System.Text.Json;

namespace _01_02_zadanie.Tools;

/// <summary>
/// Submits the final "findhim" answer to the AI_devs hub (/verify).
/// The hub response (including the flag on success) is returned to the model verbatim.
/// </summary>
internal class SubmitFindhimReportTool(string apiKey) : ITool
{
    private const string VerifyEndpoint = "https://hub.ag3nts.org/verify";
    private const string TaskName = "findhim";

    public string Name => "submit_findhim_report";

    public string Description => "Submits the final report for the 'findhim' task to the central hub. Call it exactly once, only after you have identified the suspect who was closest to one of the power plants and retrieved their access level.";

    public JsonElement ParametersSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "name": { "type": "string", "description": "First name of the identified suspect." },
            "surname": { "type": "string", "description": "Surname of the identified suspect." },
            "accessLevel": { "type": "integer", "description": "Access level returned by get_person_access_level." },
            "powerPlant": { "type": "string", "description": "Code of the power plant the suspect was closest to, format PWR0000PL." }
          },
          "required": ["name", "surname", "accessLevel", "powerPlant"]
        }
        """);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        string? name = null;
        string? surname = null;
        int? accessLevel = null;
        string? powerPlant = null;

        try
        {
            var args = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
            if (args.TryGetProperty("name", out var nameProp))
            {
                name = nameProp.GetString();
            }
            if (args.TryGetProperty("surname", out var surnameProp))
            {
                surname = surnameProp.GetString();
            }
            if (args.TryGetProperty("accessLevel", out var accessLevelProp))
            {
                accessLevel = accessLevelProp.GetInt32();
            }
            if (args.TryGetProperty("powerPlant", out var powerPlantProp))
            {
                powerPlant = powerPlantProp.GetString();
            }
        }
        catch
        {
            // ignore malformed arguments
        }

        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(surname) || accessLevel is null || string.IsNullOrEmpty(powerPlant))
        {
            return "Invalid input. Please provide name, surname, accessLevel (integer) and powerPlant (PWR0000PL code).";
        }

        var payload = new
        {
            apikey = apiKey,
            task = TaskName,
            answer = new { name, surname, accessLevel, powerPlant }
        };

        Console.WriteLine($"  >> Submitting report: {name} {surname}, accessLevel={accessLevel}, powerPlant={powerPlant}");

        using var httpClient = new HttpClient();
        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var response = await httpClient.PostAsync(VerifyEndpoint, content, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return $"Hub rejected the report. Status code: {response.StatusCode}. Response: {responseBody}";
        }

        return $"Hub response: {responseBody}";
    }
}
