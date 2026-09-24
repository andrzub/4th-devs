using System.Text.Json;
using _04_04_zadanie.Mission;

namespace _04_04_zadanie.Tools;

/// <summary>Reads back a file of the projection, for checking what was actually written.</summary>
public sealed class ReadFileTool(FilingState state) : ITool
{
    public string Name => "read_file";

    public string Description => "Reads one file of the filesystem being built, exactly as it will be sent.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Absolute file path." }
          },
          "required": ["path"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var path = ToolArguments.Parse(argumentsJson).GetString("path") ?? string.Empty;

        try
        {
            return Task.FromResult($"=== {path} ==={Environment.NewLine}{state.Filesystem.ReadFile(path)}");
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException)
        {
            return Task.FromResult(ex.Message);
        }
    }
}
