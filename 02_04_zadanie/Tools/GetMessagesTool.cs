using System.Text.Json;
using _02_04_zadanie.Mailbox;

namespace _02_04_zadanie.Tools;

/// <summary>
/// Step two of every read: the bodies. Batching is encouraged in the description because one
/// call for ten messages costs one request against the mailbox budget, while ten calls cost ten.
/// </summary>
public sealed class GetMessagesTool(MessageStore store, ZmailClient zmail, int maxBodyChars) : ITool
{
    private const int MaxIdsPerCall = 10;

    public string Name => "get_messages";

    public string Description =>
        "Returns the full body of one or more messages. Pass every message you want in a single call: " +
        "one call for ten messages costs one request, ten calls cost ten. " +
        "Always identify a message by its 32-character messageID, not by rowID: the mailbox is live and " +
        "rowID values shift as new mail arrives. Bodies already fetched by another agent are served from " +
        "a shared cache at no cost.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse($$"""
        {
          "type": "object",
          "properties": {
            "message_ids": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Up to {{MaxIdsPerCall}} identifiers, each a 32-character messageID from a listing."
            },
            "refresh": {
              "type": "boolean",
              "description": "Re-download instead of using the shared cache. Only needed if you suspect a message changed."
            }
          },
          "required": ["message_ids"]
        }
        """).RootElement;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = ToolArguments.Parse(argumentsJson);
        var ids = args.GetStringArray("message_ids");
        if (ids is null || ids.Count == 0)
            return "Missing 'message_ids'. Pass the 32-character messageID values from a listing.";

        if (ids.Count > MaxIdsPerCall)
            return $"Too many identifiers ({ids.Count}). Ask for at most {MaxIdsPerCall} per call.";

        var result = await store.GetAsync(ids, args.GetBool("refresh") ?? false, cancellationToken);
        return MailRenderer.RenderMessages(result, maxBodyChars) + $"\n{zmail.BudgetLine}";
    }
}
