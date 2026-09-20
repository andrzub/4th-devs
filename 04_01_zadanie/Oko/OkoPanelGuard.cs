namespace _04_01_zadanie.Oko;

public sealed record PathVerdict(bool Allowed, string Path, string Reason);

/// <summary>
/// The only paths this run is allowed to request from the operator console. The console is a
/// reconnaissance surface and nothing else: every change goes through the okoeditor API, and a
/// single request to the wrong path is unrecoverable, because "/edit/&lt;id&gt;" and
/// "/delete/&lt;id&gt;" are plain links answering GET with no confirmation step. The status of a
/// task is such a link too, sitting inside the detail view next to the text worth reading.
/// Leaving that to the prompt would mean trusting the model not to follow a link; the whitelist
/// means it cannot.
/// </summary>
public static class OkoPanelGuard
{
    private const string MutatingPathReason =
        "refused: '{0}' writes to the console. The web interface is read-only for this run — /edit/ and /delete/ answer a plain GET, " +
        "so one request changes or destroys the record and tells the operators someone was here. Every change goes through the okoeditor API instead.";

    public static readonly string[] ListingPaths = ["/", "/incydenty", "/notatki", "/zadania", "/uzytkownicy"];

    public static PathVerdict Evaluate(string? requested)
    {
        var path = (requested ?? string.Empty).Trim();

        if (path.Length == 0)
            return new PathVerdict(false, path, "refused: empty path.");

        if (path.Contains("://") || path.StartsWith("//"))
            return new PathVerdict(false, path, "refused: absolute URLs are not accepted, only paths of the console itself (e.g. '/incydenty').");

        if (!path.StartsWith('/'))
            path = "/" + path;

        if (path.AsSpan().ContainsAny(['?', '#', '\\', ' ']))
            return new PathVerdict(false, path, "refused: the path carries a query string, fragment or whitespace. The console needs none of them.");

        if (path.Contains(".."))
            return new PathVerdict(false, path, "refused: the path walks up the tree.");

        var normalised = path.Length > 1 ? path.TrimEnd('/') : path;
        var segments = normalised.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length > 0 && segments[0].Equals("edit", StringComparison.OrdinalIgnoreCase))
            return new PathVerdict(false, normalised, string.Format(MutatingPathReason, normalised));

        if (segments.Length > 0 && segments[0].Equals("delete", StringComparison.OrdinalIgnoreCase))
            return new PathVerdict(false, normalised, string.Format(MutatingPathReason, normalised));

        if (segments.Length == 0)
            return new PathVerdict(true, "/", "allowed: the console index, which lists the incidents.");

        if (segments.Length == 1 && OkoPages.Exists(segments[0]))
            return new PathVerdict(true, "/" + segments[0].ToLowerInvariant(), $"allowed: listing of '{segments[0].ToLowerInvariant()}'.");

        if (segments.Length == 2 && OkoPages.Exists(segments[0]))
        {
            if (!IsRecordId(segments[1]))
                return new PathVerdict(false, normalised, $"refused: '{segments[1]}' is not a record id. Ids are exactly 32 hexadecimal characters and come from a listing.");

            return new PathVerdict(true, $"/{segments[0].ToLowerInvariant()}/{segments[1].ToLowerInvariant()}", $"allowed: detail view of {segments[0].ToLowerInvariant()}/{segments[1].ToLowerInvariant()}.");
        }

        return new PathVerdict(false, normalised, $"refused: '{normalised}' is not part of the console. Readable paths are {string.Join(", ", ListingPaths)} and '/<page>/<id>'.");
    }

    public static bool IsRecordId(string? id) => id is { Length: 32 } && id.All(char.IsAsciiHexDigit);
}
