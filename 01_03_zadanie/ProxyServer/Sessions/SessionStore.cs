using System.Collections.Concurrent;
using ProxyServer.Llm;

namespace ProxyServer.Sessions;

/// <summary>
/// In-memory conversation history keyed by sessionID. Several operators may talk to the proxy
/// at once, so each session owns its own history and is processed one request at a time.
/// </summary>
public sealed class SessionStore
{
    private readonly ConcurrentDictionary<string, ChatSession> _sessions = new(StringComparer.Ordinal);

    public ChatSession GetOrCreate(string sessionId) => _sessions.GetOrAdd(sessionId, id => new ChatSession(id));

    public int Count => _sessions.Count;
}

public sealed class ChatSession(string sessionId)
{
    /// <summary>Older turns are dropped past this many messages to keep the context bounded.</summary>
    private const int MaxMessages = 60;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public string SessionId { get; } = sessionId;

    /// <summary>Conversation history without the system prompt — that one is prepended per request.</summary>
    public List<Message> History { get; } = [];

    public DateTimeOffset LastActivity { get; private set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Serialises requests within one session so a follow-up message never races with the
    /// tool loop still appending to the same history.
    /// </summary>
    public async Task<IDisposable> LockAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        return new Releaser(_gate);
    }

    public void Touch() => LastActivity = DateTimeOffset.UtcNow;

    /// <summary>
    /// Trims the history to whole turns — dropping a tool result without its assistant message
    /// would make the next request invalid.
    /// </summary>
    public void TrimIfNeeded()
    {
        if (History.Count <= MaxMessages)
            return;

        var firstKept = History.Count - MaxMessages;
        while (firstKept < History.Count && History[firstKept].Role != MessageRole.User)
            firstKept++;

        if (firstKept >= History.Count)
            return;

        History.RemoveRange(0, firstKept);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        public void Dispose() => gate.Release();
    }
}
