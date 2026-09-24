using System.Text.Json;
using _04_04_zadanie.Mission;

namespace _04_04_zadanie.Tools;

/// <summary>Lists a directory of the projection. Free: the projection is local.</summary>
public sealed class ListFilesTool(FilingState state) : ITool
{
    public string Name => "list_files";

    public string Description => "Lists the entries of a directory of the filesystem being built. Subdirectories end with '/'.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Absolute directory path. Defaults to '/'." }
          },
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var path = ToolArguments.Parse(argumentsJson).GetString("path") ?? "/";

        try
        {
            var entries = state.Filesystem.List(path);
            return Task.FromResult(entries.Count == 0 ? $"{path}: empty." : $"{path}:{Environment.NewLine}{string.Join(Environment.NewLine, entries)}");
        }
        catch (Exception ex) when (ex is ArgumentException or DirectoryNotFoundException)
        {
            return Task.FromResult(ex.Message);
        }
    }
}
