using System.Text.Json;
using _04_01_zadanie.Mission;
using _04_01_zadanie.Oko;

namespace _04_01_zadanie.Tools;

/// <summary>
/// Reads one record in full. A listing truncates the description, and the run has to know the whole
/// text before rewriting it: what a record says is the only evidence of which city it is about.
/// </summary>
public sealed class ReadRecordTool(OkoPanelClient panel, MissionState state) : ITool
{
    public string Name => "read_record";

    public string Description =>
        "Reads one record of the console in full, by the page it is listed on and its id. " +
        "The same id addresses a different record on each page, so both are required.";

    public JsonElement ParametersSchema => JsonDocument.Parse($$"""
        {
          "type": "object",
          "properties": {
            "page": { "type": "string", "enum": [{{string.Join(", ", OkoPages.All.Select(page => $"\"{page}\""))}}], "description": "The page the record is listed on." },
            "id": { "type": "string", "description": "The record id, exactly as the listing printed it." }
          },
          "required": ["page", "id"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var arguments = ToolArguments.Parse(argumentsJson);
        var page = arguments.GetString("page");
        var id = arguments.GetString("id");

        if (!OkoPages.Exists(page))
            return $"'{page}' is not a page of the console. Pages: {string.Join(", ", OkoPages.All)}.";

        if (!OkoPanelGuard.IsRecordId(id))
            return $"'{id}' is not a record id. Ids are exactly 32 hexadecimal characters and come from a listing.";

        var record = await panel.ReadAsync(page!, id!, cancellationToken);
        state.Remember([record]);

        return record.Describe();
    }
}
