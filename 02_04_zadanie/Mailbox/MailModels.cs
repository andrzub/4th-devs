namespace _02_04_zadanie.Mailbox;

/// <summary>
/// List entry as returned by getInbox, getThread and search: headers only, no body.
/// </summary>
/// <param name="RowId">
/// Positional identifier. It is NOT stable: the mailbox is live and rows shift as new mail
/// arrives, so the same message was observed as rowID 127 and later as 130.
/// </param>
/// <param name="MessageId">32-character hash. The only stable handle for a message.</param>
public sealed record MailHeader(string RowId, string MessageId, string ThreadId, string Subject, string From, string To, string Date)
{
    public string ToLine() => $"[{MessageId}] {Date} | thread {ThreadId} | from: {From} | to: {To} | {Subject}";
}

/// <summary>One message with its body, as returned by getMessages.</summary>
public sealed record MailMessage(MailHeader Header, string Body)
{
    public string Render(int maxBodyChars = int.MaxValue) =>
        $"""
         messageID: {Header.MessageId}
         thread:    {Header.ThreadId}
         date:      {Header.Date}
         from:      {Header.From}
         to:        {Header.To}
         subject:   {Header.Subject}
         body:
         {ZmailParser.Truncate(Body, maxBodyChars)}
         """;
}

public sealed record MailPage(int Page, int PerPage, int Total, int TotalPages)
{
    public string Describe() => $"page {Page} of {TotalPages} ({PerPage} per page, {Total} messages match)";
}

/// <summary>
/// Thrown when the configured zmail request budget is used up. Fatal for a run: the agent
/// loop stops instead of hammering an API that answers with an error from now on.
/// </summary>
public sealed class ZmailBudgetExceededException(string message) : Exception(message);
