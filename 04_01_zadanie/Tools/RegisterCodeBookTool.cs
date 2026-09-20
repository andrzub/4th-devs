using System.Text.Json;
using _04_01_zadanie.Mission;

namespace _04_01_zadanie.Tools;

/// <summary>
/// Where the agent writes down the classification table it found in the operators' own notes. The
/// tool checks the shape of what it registers and never the truth of it: code has no business
/// knowing what a given code means, but a table with one family in it is a sign the agent guessed
/// the entry it wanted instead of reading the table, and from here on every incident title is
/// checked against what was registered.
/// </summary>
public sealed class RegisterCodeBookTool(MissionState state, string? savePath = null) : ITool
{
    public string Name => "register_codebook";

    public string Description =>
        "Registers the incident classification table exactly as the console's own notes describe it. " +
        "Register the whole table, every family and every subtype, not only the entry you intend to use. " +
        "Until it is registered, no incident title can be changed.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "families": {
              "type": "array",
              "description": "Every code family the note lists.",
              "items": {
                "type": "object",
                "properties": {
                  "prefix": { "type": "string", "description": "The four-letter family prefix, e.g. PROB." },
                  "meaning": { "type": "string", "description": "What the note says the family stands for." },
                  "subtypes": {
                    "type": "array",
                    "items": {
                      "type": "object",
                      "properties": {
                        "code": { "type": "string", "description": "The two digits of the subtype, e.g. 01." },
                        "meaning": { "type": "string", "description": "What the note says the subtype stands for." }
                      },
                      "required": ["code", "meaning"],
                      "additionalProperties": false
                    }
                  }
                },
                "required": ["prefix", "meaning", "subtypes"],
                "additionalProperties": false
              }
            }
          },
          "required": ["families"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        JsonElement registration;
        try
        {
            registration = JsonDocument.Parse(string.IsNullOrWhiteSpace(argumentsJson) ? "{}" : argumentsJson).RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Task.FromResult($"The registration is not valid JSON: {ex.Message}");
        }

        if (!state.TryRegisterCodeBook(registration, out var error))
            return Task.FromResult($"Registration refused: {error}");

        if (savePath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(savePath))!);
            File.WriteAllText(savePath, registration.GetRawText());
        }

        return Task.FromResult($"Classification table registered. Incident titles will be checked against it:{Environment.NewLine}{state.CodeBook!.Render()}");
    }
}
