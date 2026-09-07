using System.Text;

namespace _02_04_zadanie.Mailbox;

/// <summary>
/// Renders zmail results for a model. Listings become one line per message instead of escaped
/// JSON, which costs a fraction of the tokens and keeps the stable identifier in plain sight.
/// Error bodies are never reformatted: the API's own wording is the most precise feedback there is.
/// </summary>
public static class MailRenderer
{
    public static string RenderHeaders(string json, string heading)
    {
        if (ZmailParser.IsError(json, out var error))
            return $"{heading} failed: {error}";

        var (headers, page) = ZmailParser.ParseHeaders(json);
        if (headers.Count == 0)
            return $"{heading}: no messages matched. The mailbox is live, so this can change; a different query may also work.";

        var sb = new StringBuilder();
        sb.AppendLine($"{heading}: {headers.Count} message(s){(page is null ? "" : ", " + page.Describe())}.");
        foreach (var header in headers)
            sb.AppendLine(header.ToLine());

        if (page is { } p && p.Page < p.TotalPages)
            sb.AppendLine($"More results on pages {p.Page + 1}..{p.TotalPages}.");

        sb.Append("Bodies are not included. Pass the bracketed messageID values to get_messages before drawing any conclusion.");
        return sb.ToString();
    }

    public static string RenderMessages(MessageFetchResult result, int maxBodyChars)
    {
        if (result.Error is not null)
            return $"get_messages failed: {result.Error}";

        var sb = new StringBuilder();
        sb.AppendLine($"{result.Messages.Count} message(s): {result.FetchedFromApi} downloaded, {result.ServedFromCache} already in the shared cache.");

        if (result.NotFound.Count > 0)
            sb.AppendLine($"No message exists for: {string.Join(", ", result.NotFound)}.");

        foreach (var message in result.Messages)
        {
            sb.AppendLine();
            sb.AppendLine("-----");
            sb.AppendLine(message.Render(maxBodyChars));
        }

        if (result.Unsolicited.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"WARNING: the API also returned {result.Unsolicited.Count} message(s) matching none of the " +
                          "identifiers you asked for. Do not treat what follows as an answer to your request. Before you " +
                          "use anything from it, find the same message through a search or a listing and read it there.");
            foreach (var message in result.Unsolicited)
            {
                sb.AppendLine();
                sb.AppendLine("----- unrequested -----");
                sb.AppendLine(message.Render(maxBodyChars));
            }
        }

        return sb.ToString().TrimEnd();
    }
}
