using System.Text.Json;
using _04_01_zadanie.Hub;
using _04_01_zadanie.Mission;
using _04_01_zadanie.Oko;

namespace _04_01_zadanie.Tools;

/// <summary>
/// The only way this run changes anything. Every call passes <see cref="UpdateGuard"/> first, so a
/// malformed or misaddressed edit costs a turn instead of a request and a live record. In a dry run
/// the edit is judged exactly the same way but never sent, and the checklist advances on the
/// projection — a full rehearsal that leaves the console untouched.
/// </summary>
public sealed class UpdateRecordTool(OkoEditorClient client, MissionState state, bool dryRun) : ITool
{
    public string Name => "update_record";

    public string Description =>
        "Sends one edit to the okoeditor API. Give the page, the id, and whichever of title, content or done you are changing " +
        "(at least one of title or content; done is only accepted on " + OkoPages.Tasks + "). " +
        "Fields you leave out keep their current value. Every call spends one of the run's requests.";

    public JsonElement ParametersSchema => JsonDocument.Parse($$"""
        {
          "type": "object",
          "properties": {
            "page": { "type": "string", "enum": [{{string.Join(", ", OkoPages.Editable.Select(page => $"\"{page}\""))}}], "description": "The page the record is listed on." },
            "id": { "type": "string", "description": "The record id as the listing of that page printed it." },
            "title": { "type": "string", "description": "The new title. For an incident it must begin with a code from the registered classification table." },
            "content": { "type": "string", "description": "The new description, in Polish, in the style of the console." },
            "done": { "type": "string", "enum": ["YES", "NO"], "description": "Task status, accepted only on page {{OkoPages.Tasks}}." }
          },
          "required": ["page", "id"],
          "additionalProperties": false
        }
        """).RootElement.Clone();

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var arguments = ToolArguments.Parse(argumentsJson);
        var requested = new UpdateRequest(
            arguments.GetString("page") ?? string.Empty,
            arguments.GetString("id") ?? string.Empty,
            arguments.GetString("title"),
            arguments.GetString("content"),
            arguments.GetString("done"));

        var verdict = UpdateGuard.Evaluate(requested, state);

        if (!verdict.Allowed)
        {
            state.Record(requested, sent: false, verdict.Reason);
            return $"{verdict.Reason}{Environment.NewLine}Nothing was sent; this cost no request.";
        }

        var request = verdict.Request!;

        if (dryRun)
        {
            state.Apply(request);
            state.Record(request, sent: false, "dry run: accepted locally, nothing was sent.");
            return $"DRY RUN — the edit passed every check and was not sent. The console still shows the old text.{Environment.NewLine}{Describe(request)}";
        }

        var reply = await client.UpdateAsync(request.Page, request.Id, request.Title, request.Content, request.Done, cancellationToken);
        state.ScanForFlag(reply.Body);

        if (reply.IsSuccess)
            state.Apply(request);

        state.Record(request, sent: true, $"HTTP {reply.Status}: {reply.Body}");

        return $"HTTP {reply.Status}{Environment.NewLine}{reply.Body}";
    }

    private static string Describe(UpdateRequest request)
    {
        var fields = new List<string>();
        if (request.Title is not null)
            fields.Add($"title: {request.Title}");
        if (request.Content is not null)
            fields.Add($"content: {request.Content}");
        if (request.Done is not null)
            fields.Add($"done: {request.Done}");

        return $"{request.Page}/{request.Id}{Environment.NewLine}  {string.Join(Environment.NewLine + "  ", fields)}";
    }
}
