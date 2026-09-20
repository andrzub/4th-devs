using System.Text;
using System.Text.Json;
using _04_01_zadanie.Mission;
using _04_01_zadanie.Oko;

namespace _04_01_zadanie.Tools;

/// <summary>
/// Lists one page of the operator console. Reading is the only thing the interface is good for
/// here, and it is also where the identifiers come from: an id is only usable on the page it was
/// listed on.
/// </summary>
public sealed class ReadConsoleTool(OkoPanelClient panel, MissionState state) : ITool
{
    public string Name => "read_console";

    public string Description =>
        "Lists one page of the OKO operator console: " + string.Join(", ", OkoPages.All) +
        ". Returns every entry with its id, title and the beginning of its description. Reading never changes anything.";

    public JsonElement ParametersSchema => JsonDocument.Parse($$"""
        {
          "type": "object",
          "properties": {
            "page": { "type": "string", "enum": [{{string.Join(", ", OkoPages.All.Select(page => $"\"{page}\""))}}], "description": "Which page to list." }
          },
          "required": ["page"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var page = ToolArguments.Parse(argumentsJson).GetString("page");

        if (!OkoPages.Exists(page))
            return $"'{page}' is not a page of the console. Pages: {string.Join(", ", OkoPages.All)}.";

        var records = await panel.ListAsync(page!, cancellationToken);
        state.Remember(records);

        if (records.Count == 0)
            return $"The '{page}' page lists no records that can be addressed by id.";

        var builder = new StringBuilder($"{page} ({records.Count} entries):");
        builder.AppendLine();

        foreach (var record in records)
            builder.AppendLine(record.Describe());

        return builder.ToString().TrimEnd();
    }
}
