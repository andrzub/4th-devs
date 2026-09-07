using System.Text.Json;
using _02_04_zadanie.Mailbox;

namespace _02_04_zadanie.Tools;

/// <summary>
/// Every message of one conversation. A reply often carries the detail the first message only
/// promised, so a thread found through search is worth opening in full.
/// </summary>
public sealed class GetThreadTool(ZmailClient zmail) : ITool
{
    public string Name => "get_thread";

    public string Description =>
        "Lists every message in one conversation, headers only. Use it when a search hit looks relevant: " +
        "replies in the same thread often carry the detail the first message left out.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "thread_id": {
              "type": "string",
              "description": "Numeric thread identifier, taken from a listing."
            }
          },
          "required": ["thread_id"]
        }
        """).RootElement;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = ToolArguments.Parse(argumentsJson);
        var threadId = args.GetString("thread_id")?.Trim() ?? args.GetInt("thread_id")?.ToString();
        if (string.IsNullOrEmpty(threadId))
            return "Missing 'thread_id'.";

        var json = await zmail.GetThreadAsync(threadId, cancellationToken);
        return MailRenderer.RenderHeaders(json, $"thread {threadId}") + $"\n{zmail.BudgetLine}";
    }
}
