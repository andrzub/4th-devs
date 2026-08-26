using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace _01_04_zadanie.Tools;

/// <summary>
/// Finalises the declaration: always writes it to disk, and posts it to the hub only when the
/// run was started with --submit. Without that flag the agent is told the text was recorded but
/// not sent, so a plain run produces a reviewable document and never touches /verify.
/// </summary>
public sealed class SubmitDeclarationTool : ITool
{
    private const string TaskName = "sendit";
    private const string VerifyUrl = "https://hub.ag3nts.org/verify";

    private readonly string _outputPath;
    private readonly string _aiDevsApiKey;
    private readonly bool _submitEnabled;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public SubmitDeclarationTool(string outputPath, string aiDevsApiKey, bool submitEnabled)
    {
        _outputPath = outputPath;
        _aiDevsApiKey = aiDevsApiKey;
        _submitEnabled = submitEnabled;
    }

    /// <summary>Set once the hub has accepted the declaration, so the loop can stop.</summary>
    public bool Accepted { get; private set; }

    public string Name => "submit_declaration";

    public string Description =>
        "Record the finished declaration. Pass the complete declaration text, formatted exactly like the " +
        "template from the documentation. Call this once you are confident every field is correct.";

    public JsonElement ParametersSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "declaration": {
              "type": "string",
              "description": "The full declaration text, including the header, all field lines and the separator lines."
            }
          },
          "required": ["declaration"],
          "additionalProperties": false
        }
        """);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        string declaration;
        try
        {
            using var args = JsonDocument.Parse(argumentsJson);
            declaration = args.RootElement.GetProperty("declaration").GetString() ?? "";
        }
        catch (Exception ex)
        {
            return $"Could not read the arguments: {ex.Message}";
        }

        if (declaration.Trim().Length == 0)
            return "The declaration text is empty.";

        await File.WriteAllTextAsync(_outputPath, declaration, new UTF8Encoding(false), cancellationToken);

        Console.WriteLine();
        Console.WriteLine("=== DEKLARACJA ===");
        Console.WriteLine(declaration);
        Console.WriteLine("==================");
        Console.WriteLine($"(zapisano do {_outputPath})");
        Console.WriteLine();

        if (!_submitEnabled)
        {
            Accepted = true;
            return $"Declaration recorded and saved to {_outputPath}. Sending is disabled for this run, so it was NOT posted to the hub. " +
                   "Nothing further to do — finish with a short summary of how you derived each field.";
        }

        return await PostToHubAsync(declaration, cancellationToken);
    }

    private async Task<string> PostToHubAsync(string declaration, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["apikey"] = _aiDevsApiKey,
            ["task"]   = TaskName,
            ["answer"] = new JsonObject { ["declaration"] = declaration }
        }.ToJsonString();

        try
        {
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync(VerifyUrl, content, cancellationToken);

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"  hub {(int)response.StatusCode}: {body}");

            if (response.IsSuccessStatusCode)
            {
                Accepted = true;
                return $"The hub accepted the declaration. Response: {body}";
            }

            return $"The hub rejected the declaration (HTTP {(int)response.StatusCode}). Response: {body}\n" +
                   "Read the message carefully — it names what is wrong. Fix that field and call submit_declaration again.";
        }
        catch (Exception ex)
        {
            return $"Could not reach the hub: {ex.Message}";
        }
    }
}
