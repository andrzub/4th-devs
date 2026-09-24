using System.Text.Json;
using _04_04_zadanie.Filesystem;
using _04_04_zadanie.Mission;

namespace _04_04_zadanie.Tools;

/// <summary>
/// The one way content enters the projection. Every write is judged by the guard first; a refusal
/// explains the rule and costs a tool turn, which is the whole point of building locally.
/// </summary>
public sealed class WriteFileTool(FilingState state) : ITool
{
    public const string ToolName = "write_file";

    public string Name => ToolName;

    public string Description =>
        "Creates or replaces one file of the filesystem being built, after checking it against the map of content's rules. " +
        "Refused writes explain what to change. Directories cannot be created; files go straight into /miasta, /osoby or /towary.";

    public JsonElement ParametersSchema => JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "path": { "type": "string", "description": "Absolute path of the file, e.g. /miasta/komarowo." },
            "content": { "type": "string", "description": "The whole content of the file, exactly as it should be stored." }
          },
          "required": ["path", "content"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var arguments = ToolArguments.Parse(argumentsJson);
        var path = arguments.GetString("path");
        var content = arguments.GetString("content");

        var verdict = WriteGuard.Evaluate(state.Filesystem, path, content);
        if (!verdict.Allowed)
        {
            state.Refusals++;
            return Task.FromResult($"Refused {verdict.Path}: {verdict.Reason}");
        }

        var replaced = state.Filesystem.WriteFile(verdict.Path, content!);
        state.Writes++;
        if (replaced)
            state.Replacements++;

        return Task.FromResult($"{(replaced ? "Replaced" : "Written")} {verdict.Path}: {verdict.Reason}");
    }
}
