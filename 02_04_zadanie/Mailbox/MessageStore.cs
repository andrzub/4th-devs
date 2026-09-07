using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace _02_04_zadanie.Mailbox;

/// <summary>
/// Shared read-through cache of message bodies, held by every researcher at once. Two agents
/// chasing different facts through the same thread pay for one download, not two.
/// Caching is safe here because the mailbox is read-only for us and message bodies do not
/// change; only the set of messages grows. The listing does expose a 'modifyHash' per message,
/// so a caller that suspects an edit can force a re-download.
/// </summary>
public sealed partial class MessageStore(ZmailClient zmail)
{
    [GeneratedRegex("^[0-9a-fA-F]{32}$")]
    private static partial Regex MessageIdRegex();

    private readonly ConcurrentDictionary<string, MailMessage> _byMessageId = new(StringComparer.OrdinalIgnoreCase);

    public int CachedCount => _byMessageId.Count;

    public async Task<MessageFetchResult> GetAsync(IReadOnlyList<string> ids, bool refresh, CancellationToken cancellationToken = default)
    {
        var wanted = ids.Select(id => id.Trim()).Where(id => id.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (wanted.Count == 0)
            return new MessageFetchResult([], [], [], 0, 0, "No identifiers given.");

        var fromCache = new List<MailMessage>();
        var toFetch = new List<string>();

        foreach (var id in wanted)
        {
            // Only the 32-character hash is a stable handle. A numeric rowID points at a position
            // that shifts as new mail arrives, so it is never served from, or written to, the cache.
            if (!refresh && IsMessageId(id) && _byMessageId.TryGetValue(id, out var cached))
                fromCache.Add(cached);
            else
                toFetch.Add(id);
        }

        var fetched = new List<MailMessage>();
        var unsolicited = new List<MailMessage>();
        var notFound = new List<string>();
        string? error = null;

        if (toFetch.Count > 0)
        {
            var json = await zmail.GetMessagesAsync(toFetch, cancellationToken);
            if (ZmailParser.IsError(json, out var apiError))
            {
                error = apiError;
            }
            else
            {
                var (messages, missing) = ZmailParser.ParseMessages(json);
                var requested = toFetch.ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var message in messages)
                {
                    if (IsMessageId(message.Header.MessageId))
                        _byMessageId[message.Header.MessageId] = message;

                    // The API also answers to numeric rowIDs, and an identifier that is not a real
                    // message can still land on one: an id of 32 zeros reads as rowID 0, which holds a
                    // message that appears in no listing. So anything that matches neither the hash
                    // nor the rowID we asked for is separated out instead of being presented as an answer.
                    if (requested.Contains(message.Header.MessageId) || requested.Contains(message.Header.RowId))
                        fetched.Add(message);
                    else
                        unsolicited.Add(message);
                }

                notFound.AddRange(missing);
                var returned = messages.Select(m => m.Header.MessageId)
                    .Concat(messages.Select(m => m.Header.RowId))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                notFound.AddRange(toFetch.Where(id => !returned.Contains(id)));
            }
        }

        var all = fromCache.Concat(fetched)
            .OrderBy(m => m.Header.Date, StringComparer.Ordinal)
            .ToList();

        return new MessageFetchResult(all, unsolicited, notFound.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            fromCache.Count, fetched.Count, error);
    }

    private static bool IsMessageId(string id) => MessageIdRegex().IsMatch(id);
}

public sealed record MessageFetchResult(
    IReadOnlyList<MailMessage> Messages,
    IReadOnlyList<MailMessage> Unsolicited,
    IReadOnlyList<string> NotFound,
    int ServedFromCache,
    int FetchedFromApi,
    string? Error);
