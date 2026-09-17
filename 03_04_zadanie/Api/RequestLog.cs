using System.Text.Json;
using System.Text.RegularExpressions;

namespace _03_04_zadanie.Api;

/// <summary>
/// Append-only record of what the central's agent asked, what it was told, and what went to
/// /verify. Its queries are the only evidence of how it phrases things, and they arrive once.
/// The key and any flag the central returns are masked on the way in — the log is meant to be
/// readable and shareable, and neither of those belongs in a repository.
/// </summary>
public sealed partial class RequestLog
{
    private readonly object gate = new();
    private readonly string path;

    public RequestLog(string path) => this.path = path;

    public void Write(string route, string body, string? parameters, string output, string? error)
    {
        Append(new
        {
            at = DateTimeOffset.Now.ToString("O"),
            route,
            body,
            parameters,
            output,
            outputBytes = output.Length,
            error,
        });
    }

    public void WriteVerify(string kind, string payload, int statusCode, string response)
    {
        Append(new
        {
            at = DateTimeOffset.Now.ToString("O"),
            verify = kind,
            payload = Redact(payload),
            statusCode,
            response = Redact(response),
        });
    }

    private void Append(object entry)
    {
        var line = JsonSerializer.Serialize(entry);

        lock (gate)
        {
            File.AppendAllText(path, line + Environment.NewLine);
        }
    }

    private static string Redact(string text)
    {
        var withoutKey = ApiKeyPattern().Replace(text, @"""apikey"": ""***""");
        return FlagPattern().Replace(withoutKey, "{{FLG:***}}");
    }

    [GeneratedRegex(@"""apikey""\s*:\s*""[^""]*""")]
    private static partial Regex ApiKeyPattern();

    [GeneratedRegex(@"\{\{?FLG:[^}]*\}\}?")]
    private static partial Regex FlagPattern();
}
