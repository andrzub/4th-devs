using System.Text.Json;
using _02_04_zadanie.Mailbox;

namespace _02_04_zadanie.Tools;

/// <summary>Paged listing of the whole mailbox, for when search terms are not obvious yet.</summary>
public sealed class GetInboxTool(ZmailClient zmail) : ITool
{
    public string Name => "get_inbox";

    public string Description =>
        "Lists the mailbox in date order and returns headers only (date, sender, recipient, subject, messageID). " +
        "Use it to get a feel for what is in there, or to check whether new mail has arrived since your last look. " +
        "Prefer search_mail once you know what to look for: paging through the whole mailbox costs many requests.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "page": {
              "type": "integer",
              "description": "Page number, 1-based. Default 1. Page 1 holds the newest messages."
            },
            "per_page": {
              "type": "integer",
              "description": "Messages per page, between 5 and 20. Default 20."
            }
          }
        }
        """).RootElement;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = ToolArguments.Parse(argumentsJson);
        var page = Math.Max(1, args.GetInt("page") ?? 1);
        var perPage = Math.Clamp(args.GetInt("per_page") ?? 20, 5, 20);

        var json = await zmail.GetInboxAsync(page, perPage, cancellationToken);
        return MailRenderer.RenderHeaders(json, $"inbox page {page}") + $"\n{zmail.BudgetLine}";
    }
}
