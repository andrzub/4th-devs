using System.Text.Json;
using _02_04_zadanie.Mailbox;

namespace _02_04_zadanie.Tools;

/// <summary>
/// The mailbox search. Returns headers only, which is what makes the two-step read-then-fetch
/// pattern explicit for the researcher.
/// </summary>
public sealed class SearchMailTool(ZmailClient zmail) : ITool
{
    public string Name => "search_mail";

    public string Description =>
        "Searches the mailbox and returns matching message headers (date, sender, recipient, subject, messageID) " +
        "without bodies. Supports Gmail-like operators: bare words, \"exact phrase\", -excluded, from:, to:, " +
        "subject:, subject:\"phrase\", OR, AND. Two terms with no operator between them mean AND. " +
        "Start broad, then narrow down; fetch bodies with get_messages afterwards.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "query": {
              "type": "string",
              "description": "Search query, e.g. 'from:proton.me' or 'subject:\"ticket\" OR haslo'."
            },
            "page": {
              "type": "integer",
              "description": "Result page, 1-based. Default 1."
            },
            "per_page": {
              "type": "integer",
              "description": "Results per page, between 5 and 20. Default 20, to keep the number of requests low."
            }
          },
          "required": ["query"]
        }
        """).RootElement;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = ToolArguments.Parse(argumentsJson);
        var query = args.GetString("query")?.Trim();
        if (string.IsNullOrEmpty(query))
            return "Missing 'query'.";

        var page = Math.Max(1, args.GetInt("page") ?? 1);
        var perPage = Math.Clamp(args.GetInt("per_page") ?? 20, 5, 20);

        var json = await zmail.SearchAsync(query, page, perPage, cancellationToken);
        return MailRenderer.RenderHeaders(json, $"search \"{query}\"") + $"\n{zmail.BudgetLine}";
    }
}
