using _04_01_zadanie.Oko;

namespace _04_01_zadanie.Mission;

public sealed record UpdateVerdict(bool Allowed, string Reason, UpdateRequest? Request = null);

/// <summary>
/// What may be sent to the okoeditor API, decided before the request leaves the process. A refusal
/// here costs one turn of the agent and no request at all, while a bad edit costs a request, a live
/// record and a trace in a console the operators are watching.
///
/// The rule that matters most is the third one: identifiers are shared between the pages, so the
/// same thirty-two characters address a different record on "incydenty", on "notatki" and on
/// "zadania". An update naming a page the agent never read that id on would not fail — it would
/// quietly rewrite something else.
/// </summary>
public static class UpdateGuard
{
    public static UpdateVerdict Evaluate(UpdateRequest request, MissionState state)
    {
        var page = (request.Page ?? string.Empty).Trim().ToLowerInvariant();
        var id = (request.Id ?? string.Empty).Trim().ToLowerInvariant();

        if (page == OkoPages.Users)
            return new UpdateVerdict(false, $"refused: '{OkoPages.Users}' is read-only in the API. Editable pages: {string.Join(", ", OkoPages.Editable)}.");

        if (!OkoPages.Editable.Contains(page))
            return new UpdateVerdict(false, $"refused: '{request.Page}' is not an editable page. Editable pages: {string.Join(", ", OkoPages.Editable)}.");

        if (!OkoPanelGuard.IsRecordId(id))
            return new UpdateVerdict(false, $"refused: '{request.Id}' is not a record id. Ids are exactly 32 hexadecimal characters, taken from a listing you read.");

        var target = state.Find(page, id);
        if (target is null)
        {
            var known = state.KnownIds(page);
            return new UpdateVerdict(false,
                $"refused: no record '{page}/{id}' has been read in this run. The same id addresses a different record on every page, so an id read elsewhere would rewrite the wrong row. " +
                (known.Count > 0 ? $"Ids read so far on '{page}': {string.Join(", ", known)}." : $"Read the '{page}' listing first."));
        }

        var title = Normalise(request.Title);
        var content = Normalise(request.Content);
        var done = Normalise(request.Done)?.ToUpperInvariant();

        if (request.Title is not null && title is null)
            return new UpdateVerdict(false, "refused: the title is empty. Send the new title, or leave the field out to keep the current one.");

        if (request.Content is not null && content is null)
            return new UpdateVerdict(false, "refused: the description is empty. Send the new text, or leave the field out to keep the current one.");

        if (title is null && content is null)
            return new UpdateVerdict(false, "refused: the API requires at least one of 'title' or 'content'.");

        if (done is not null)
        {
            if (page != OkoPages.Tasks)
                return new UpdateVerdict(false, $"refused: 'done' is only accepted on page '{OkoPages.Tasks}'.");

            if (done is not ("YES" or "NO"))
                return new UpdateVerdict(false, $"refused: 'done' takes YES or NO, not '{request.Done}'.");
        }

        if (page == OkoPages.Incidents && title is not null)
        {
            if (state.CodeBook is null)
                return new UpdateVerdict(false, "refused: the console classifies incidents with a code at the front of the title, and no classification table has been registered yet. Read the operators' note on coding and register it first.");

            var code = new OkoRecord(page, id, title).LeadingCode;

            if (code is null)
                return new UpdateVerdict(false, $"refused: '{title}' does not start with a classification code. Every incident title begins with one — four letters and two digits.");

            if (!state.CodeBook.Contains(code))
                return new UpdateVerdict(false, $"refused: '{code}' is not in the registered classification table. Codes in use:{Environment.NewLine}{state.CodeBook.Render()}");
        }

        if (IsNoOp(target, title, content, done))
            return new UpdateVerdict(false, $"refused: this update leaves '{page}/{id}' exactly as it is, and every call spends one of the run's requests.");

        return new UpdateVerdict(true, $"allowed: {page}/{id} will be updated.", new UpdateRequest(page, id, title, content, done));
    }

    private static bool IsNoOp(OkoRecord target, string? title, string? content, string? done)
    {
        var titleUnchanged = title is null || title == target.Title;
        var contentUnchanged = content is null || content == target.Content;
        var doneUnchanged = done is null || target.Done == done.Equals("YES", StringComparison.OrdinalIgnoreCase);

        return titleUnchanged && contentUnchanged && doneUnchanged;
    }

    private static string? Normalise(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
