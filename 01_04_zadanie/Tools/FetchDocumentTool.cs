using System.Text.Json;
using _01_04_zadanie.Documents;

namespace _01_04_zadanie.Tools;

/// <summary>
/// Gives the agent read access to the SPK documentation by file name.
/// Refuses image files on purpose: the agent must notice it needs the vision tool for those,
/// instead of receiving unreadable bytes and quietly moving on.
/// </summary>
public sealed class FetchDocumentTool : ITool
{
    private readonly DocumentLibrary _library;

    public FetchDocumentTool(DocumentLibrary library) => _library = library;

    public string Name => "fetch_document";

    public string Description =>
        "Fetch a text file from the SPK documentation directory and return its contents. " +
        "The entry point is 'index.md'. Documentation files reference each other with markers like " +
        "[include file=\"name.md\"] — those references are not inlined, fetch each one you need. " +
        "Graphics cannot be returned by this tool; use analyze_image for them.";

    public JsonElement ParametersSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "file": {
              "type": "string",
              "description": "Bare file name inside the documentation directory, e.g. 'index.md' or 'zalacznik-E.md'."
            }
          },
          "required": ["file"],
          "additionalProperties": false
        }
        """);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        string file;
        try
        {
            using var args = JsonDocument.Parse(argumentsJson);
            file = args.RootElement.GetProperty("file").GetString() ?? "";
        }
        catch (Exception ex)
        {
            return $"Could not read the arguments: {ex.Message}";
        }

        if (DocumentLibrary.IsImage(file))
            return $"'{file}' is a graphic, not text. Call analyze_image with this file name and a precise question about what you need from it.";

        try
        {
            return await _library.ReadTextAsync(file, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"Could not fetch '{file}': {ex.Message}";
        }
    }
}
