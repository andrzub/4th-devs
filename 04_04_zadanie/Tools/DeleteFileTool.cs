using System.Text.Json;
using _04_04_zadanie.Filesystem;
using _04_04_zadanie.Mission;

namespace _04_04_zadanie.Tools;

/// <summary>Removes a file from the projection; the three directories themselves cannot be removed.</summary>
public sealed class DeleteFileTool(FilingState state) : ITool
{
    public string Name => "delete_file";

    public string Description => "Deletes one file of the filesystem being built, e.g. one filed under the wrong name. Directories cannot be deleted.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Absolute path of the file to delete." }
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
            var normalized = VirtualFilesystem.NormalizePath(path);
            if (!state.Filesystem.FileExists(normalized))
                return Task.FromResult($"No such file: '{normalized}'. Only files can be deleted, and the directories stay.");

            state.Filesystem.Delete(normalized);
            state.Deletes++;
            return Task.FromResult($"Deleted {normalized}.");
        }
        catch (ArgumentException ex)
        {
            return Task.FromResult(ex.Message);
        }
    }
}
