using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using _04_01_zadanie.Oko;

namespace _04_01_zadanie.Mission;

public sealed record UpdateRequest(string Page, string Id, string? Title, string? Content, string? Done);

public sealed record UpdateRecord(int Number, UpdateRequest Request, bool Sent, string Outcome);

public sealed record Objective(string Key, string Description, bool Met, string Status);

/// <summary>
/// What the run knows: the records it has actually read, the classification table the agent
/// registered, and how far the errand has got. Progress is judged against a projection of the
/// console — every accepted edit is applied to the record it addressed — rather than against the
/// model's account of its own work, and the flag is picked out of the API's raw answer by a regex,
/// so a run can only end on a flag that really arrived.
/// </summary>
public sealed partial class MissionState(TaskSettings settings)
{
    [GeneratedRegex(@"\{\{?FLG:[^}]+\}\}?")]
    private static partial Regex FlagRegex();

    private static readonly string[] AnimalWords = ["zwierz", "bobr", "bóbr"];
    private static readonly string[] PeopleWords = ["czlowiek", "ludzi", "ludzie"];

    private readonly Dictionary<string, OkoRecord> _asFound = [];
    private readonly Dictionary<string, OkoRecord> _current = [];
    private readonly List<UpdateRecord> _history = [];

    public CodeBook? CodeBook { get; private set; }

    public string? Flag { get; private set; }

    public bool FlagReceived => Flag is not null;

    public IReadOnlyList<UpdateRecord> History => _history;

    public int RefusedCount => _history.Count(record => !record.Sent);

    /// <summary>How many times the checklist has turned the agent away from the verification call.</summary>
    public int FinishRefusals { get; private set; }

    // -------------------------------------------------------------------------
    // What the console held, and what it holds now
    // -------------------------------------------------------------------------

    public void Remember(IEnumerable<OkoRecord> records)
    {
        foreach (var record in records)
        {
            var key = Key(record.Page, record.Id);

            // The first reading is the console as the operators left it, and it stays: an incident
            // is identified as the one about a city by what it said before this run rewrote it.
            _asFound.TryAdd(key, record);

            // A detail view is authoritative; a listing refreshes everything but the body, which it
            // only ever carries truncated.
            if (!_current.TryGetValue(key, out var current) || record.Content.Length > 0)
                _current[key] = record;
            else
                _current[key] = current with { Title = record.Title, Summary = record.Summary, Meta = record.Meta, Done = record.Done ?? current.Done };
        }
    }

    public OkoRecord? Find(string page, string id) => _current.GetValueOrDefault(Key(page, id));

    public IReadOnlyCollection<OkoRecord> KnownRecords => _current.Values;

    public IReadOnlyList<string> KnownIds(string page) =>
        [.. _current.Values.Where(record => record.Page == page).Select(record => record.Id)];

    // -------------------------------------------------------------------------
    // The classification table
    // -------------------------------------------------------------------------

    public bool TryRegisterCodeBook(JsonElement registration, out string error)
    {
        if (!CodeBook.TryParse(registration, out var parsed, out error))
            return false;

        CodeBook = parsed;
        return true;
    }

    /// <summary>Recovers the table from a console note's own text. Used by the by-hand path and as a safety net.</summary>
    public bool TryRegisterCodeBookFromNote(string noteText, out string error)
    {
        if (!NoteCodeBookParser.TryParse(noteText, out var parsed, out error))
            return false;

        CodeBook = parsed;
        return true;
    }

    // -------------------------------------------------------------------------
    // Progress
    // -------------------------------------------------------------------------

    public UpdateRecord Record(UpdateRequest request, bool sent, string outcome)
    {
        var record = new UpdateRecord(_history.Count + 1, request, sent, outcome);
        _history.Add(record);
        return record;
    }

    /// <summary>Applies an accepted edit to the projection, so the checklist sees what the console now shows.</summary>
    public void Apply(UpdateRequest request)
    {
        var key = Key(request.Page, request.Id);
        var before = _current.GetValueOrDefault(key) ?? new OkoRecord(request.Page, request.Id, string.Empty);

        _current[key] = before with
        {
            Title = request.Title ?? before.Title,
            Content = request.Content ?? before.Content,
            Done = request.Done is null ? before.Done : request.Done.Equals("YES", StringComparison.OrdinalIgnoreCase)
        };
    }

    public IReadOnlyList<Objective> Objectives => [ReclassifyObjective(), TaskObjective(), DecoyObjective()];

    public bool AllObjectivesMet => Objectives.All(objective => objective.Met);

    public string RenderChecklist()
    {
        var builder = new StringBuilder();

        foreach (var objective in Objectives)
            builder.AppendLine($"[{(objective.Met ? "x" : " ")}] {objective.Description}{Environment.NewLine}    {objective.Status}");

        return builder.ToString().TrimEnd();
    }

    public string RenderHistory()
    {
        if (_history.Count == 0)
            return "Nothing has been sent to the okoeditor API yet.";

        var builder = new StringBuilder();
        foreach (var record in _history)
        {
            var fields = new List<string>();
            if (record.Request.Title is not null)
                fields.Add("title");
            if (record.Request.Content is not null)
                fields.Add("content");
            if (record.Request.Done is not null)
                fields.Add($"done={record.Request.Done}");

            builder.AppendLine($"#{record.Number,-3} {(record.Sent ? "sent    " : "refused ")} {record.Request.Page}/{record.Request.Id} ({string.Join(", ", fields)}){Environment.NewLine}     {record.Outcome}");
        }

        return builder.ToString().TrimEnd();
    }

    public int NoteFinishRefusal() => ++FinishRefusals;

    public void ScanForFlag(string text)
    {
        var match = FlagRegex().Match(text);
        if (match.Success)
            Flag ??= match.Value;
    }

    // -------------------------------------------------------------------------
    // The three changes the centre asked for
    // -------------------------------------------------------------------------

    /// <summary>The incident that was about the protected city when this run first read the console.</summary>
    public OkoRecord? ProtectedCityIncident => FindAsFound(OkoPages.Incidents, settings.ProtectedCity);

    /// <summary>The task that was about the protected city when this run first read the console.</summary>
    public OkoRecord? ProtectedCityTask => FindAsFound(OkoPages.Tasks, settings.ProtectedCity);

    private Objective ReclassifyObjective()
    {
        var description = $"The incident about {settings.ProtectedCity} is classified as animals, not vehicles and people.";
        var found = ProtectedCityIncident;

        if (found is null)
            return new Objective("reclassify", description, false, $"No incident about {settings.ProtectedCity} has been read yet.");

        var now = Find(found.Page, found.Id)!;
        var code = now.LeadingCode;

        if (CodeBook is null)
            return new Objective("reclassify", description, false, "The classification table has not been registered yet.");

        var met = CodeBook.MeansAnyOf(code, AnimalWords);
        return new Objective("reclassify", description, met, met
            ? $"{found.Page}/{found.Id} now reads '{code}' — {CodeBook.Describe(code)}."
            : $"{found.Page}/{found.Id} still reads '{code ?? "(no code)"}'.");
    }

    private Objective TaskObjective()
    {
        var description = $"The task about {settings.ProtectedCity} is marked done and says animals were seen there.";
        var found = ProtectedCityTask;

        if (found is null)
            return new Objective("task", description, false, $"No task about {settings.ProtectedCity} has been read yet.");

        var now = Find(found.Page, found.Id)!;
        var done = now.Done == true;
        var mentionsAnimals = TextMatch.ContainsAny(now.Content, AnimalWords);

        return new Objective("task", description, done && mentionsAnimals,
            $"{found.Page}/{found.Id}: done={(done ? "YES" : "NO")}, animals in the description: {(mentionsAnimals ? "yes" : "no")}.");
    }

    private Objective DecoyObjective()
    {
        var description = $"An incident reports people moving near {settings.DecoyTargetCity}.";

        var decoy = _current.Values.FirstOrDefault(record =>
            record.Page == OkoPages.Incidents
            && (TextMatch.Contains(record.Title, settings.DecoyTargetCity) || TextMatch.Contains(record.Content, settings.DecoyTargetCity)));

        if (decoy is null)
            return new Objective("decoy", description, false, $"No incident mentions {settings.DecoyTargetCity}.");

        var showsPeople = (CodeBook?.MeansAnyOf(decoy.LeadingCode, PeopleWords) ?? false) || TextMatch.ContainsAny(decoy.Content, PeopleWords);

        return new Objective("decoy", description, showsPeople,
            showsPeople
                ? $"{decoy.Page}/{decoy.Id} reports people near {settings.DecoyTargetCity}."
                : $"{decoy.Page}/{decoy.Id} mentions {settings.DecoyTargetCity}, but neither its code nor its description reports people moving.");
    }

    private OkoRecord? FindAsFound(string page, string city) => _asFound.Values.FirstOrDefault(record =>
        record.Page == page && (TextMatch.Contains(record.Title, city) || TextMatch.Contains(record.Content, city) || TextMatch.Contains(record.Summary, city)));

    private static string Key(string page, string id) => $"{page.ToLowerInvariant()}/{id.ToLowerInvariant()}";
}
