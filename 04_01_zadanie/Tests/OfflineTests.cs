using System.Text.Json;
using _04_01_zadanie.Mission;
using _04_01_zadanie.Oko;

namespace _04_01_zadanie.Tests;

/// <summary>
/// The rules that keep this run out of the console's write paths and turn its HTML into records,
/// checked offline against hand-built markup. Both matter before anything goes on the wire: one
/// request to "/delete/&lt;id&gt;" destroys a record and announces the intrusion, and a listing read
/// wrongly hands the model identifiers that address the wrong rows.
/// </summary>
public static class OfflineTests
{
    private const string Id = "380792b2c86d9c5be670b3bde48e187b";
    private const string OtherId = "ff3313a39099222e325f03b378680e3c";

    public static bool Run()
    {
        var results = new List<(string Case, bool Passed, string Detail)>();

        void Expect(string name, bool passed, string detail = "") => results.Add((name, passed, detail));

        void ExpectAllowed(string name, string path, string expectedPath)
        {
            var verdict = OkoPanelGuard.Evaluate(path);
            Expect(name, verdict.Allowed && verdict.Path == expectedPath, verdict.Reason);
        }

        void ExpectRefused(string name, string path, string expectedFragment)
        {
            var verdict = OkoPanelGuard.Evaluate(path);
            Expect(name, !verdict.Allowed && verdict.Reason.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase), verdict.Reason);
        }

        // --- the guard: what may be read ---------------------------------------------------------
        ExpectAllowed("the index is readable", "/", "/");
        ExpectAllowed("a listing is readable", "/incydenty", "/incydenty");
        ExpectAllowed("the read-only user page is readable", "/uzytkownicy", "/uzytkownicy");
        ExpectAllowed("a detail view is readable", $"/zadania/{Id}", $"/zadania/{Id}");
        ExpectAllowed("a trailing slash is tolerated", "/notatki/", "/notatki");
        ExpectAllowed("a missing leading slash is tolerated", "zadania", "/zadania");
        ExpectAllowed("an uppercase id is normalised", $"/incydenty/{Id.ToUpperInvariant()}", $"/incydenty/{Id}");

        // --- the guard: what may not -------------------------------------------------------------
        ExpectRefused("the edit link is refused", $"/edit/{Id}", "read-only");
        ExpectRefused("the delete link is refused", $"/delete/{Id}", "read-only");
        ExpectRefused("case does not smuggle the edit link through", $"/Edit/{Id}", "read-only");
        ExpectRefused("an edit link with no id is still an edit link", "/edit", "read-only");
        ExpectRefused("an edit link reached through a detail path is refused", $"/incydenty/{Id}/edit", "not part of the console");
        ExpectRefused("an absolute url is refused", $"https://oko.ag3nts.org/delete/{Id}", "absolute");
        ExpectRefused("a protocol-relative url is refused", "//oko.ag3nts.org/delete", "absolute");
        ExpectRefused("a path walking up the tree is refused", $"/incydenty/../delete/{Id}", "walks up");
        ExpectRefused("a query string is refused", "/zadania?action=done", "query string");
        ExpectRefused("a fragment is refused", "/zadania#done", "query string");
        ExpectRefused("an unknown page is refused", "/raporty", "not part of the console");
        ExpectRefused("a made-up id is refused", "/incydenty/1234", "not a record id");
        ExpectRefused("a non-hexadecimal id is refused", "/incydenty/zzz792b2c86d9c5be670b3bde48e187b", "not a record id");
        ExpectRefused("an empty path is refused", "", "empty");

        // --- the parser: listings -----------------------------------------------------------------
        var incidents = RecordParser.ParseListing(OkoPages.Incidents, IncidentListingHtml);
        Expect("both incidents are read", incidents.Count == 2, $"{incidents.Count} records");
        Expect("the incident id is read", incidents.Count > 0 && incidents[0].Id == Id, incidents.Count > 0 ? incidents[0].Id : "-");
        Expect("the incident title is read", incidents[0].Title == "MOVE03 Trudne do klasyfikacji ruchy nieopodal miasta Skolwin", incidents[0].Title);
        Expect("the incident summary is read", incidents[0].Summary.StartsWith("Czujniki zarejestrowaly"), incidents[0].Summary);
        Expect("the incident metadata is read", incidents[0].Meta.Contains("radar"), incidents[0].Meta);
        Expect("an incident carries no done flag", incidents[0].Done is null);

        var tasks = RecordParser.ParseListing(OkoPages.Tasks, TaskListingHtml);
        Expect("both tasks are read", tasks.Count == 2, $"{tasks.Count} records");
        Expect("a pending task reads as pending", tasks[0].Done == false, tasks[0].Done?.ToString() ?? "null");
        Expect("a finished task reads as finished", tasks[1].Done == true, tasks[1].Done?.ToString() ?? "null");
        Expect("the status word stays out of the metadata", !tasks[0].Meta.Contains("wykonane", StringComparison.OrdinalIgnoreCase), tasks[0].Meta);

        // --- the parser: detail views --------------------------------------------------------------
        var detail = RecordParser.ParseDetail(OkoPages.Incidents, Id, IncidentDetailHtml);
        Expect("the detail page names its own page", detail.Page == "incydenty", detail.Page);
        Expect("the detail title is read", detail.Title.StartsWith("MOVE03 "), detail.Title);
        Expect("the whole body is read, not the excerpt", detail.Content.Contains("niszczycieli"), detail.Content);

        var taskDetail = RecordParser.ParseDetail(OkoPages.Tasks, Id, TaskDetailHtml);
        Expect("a pending task detail reads as pending", taskDetail.Done == false, taskDetail.Done?.ToString() ?? "null");
        Expect("the task body is read", taskDetail.Content.StartsWith("Probki ruchu"), taskDetail.Content);
        Expect("the edit link in the detail view is not mistaken for content", !taskDetail.Content.Contains("/edit/"), taskDetail.Content);

        Expect("the sign-in form is recognised", RecordParser.IsLoginPage(LoginHtml));
        Expect("a listing is not mistaken for the sign-in form", !RecordParser.IsLoginPage(IncidentListingHtml));

        // --- classification codes ------------------------------------------------------------------
        Expect("a leading code is found", new OkoRecord("incydenty", Id, "MOVE03 Ruch nieopodal Skolwina").LeadingCode == "MOVE03");
        Expect("a title without a code has none", new OkoRecord("zadania", Id, "Zbadanie nagran").LeadingCode is null);
        Expect("a five-character prefix is not a code", new OkoRecord("incydenty", Id, "MOVE3 Ruch").LeadingCode is null);
        Expect("a lowercase prefix is not a code", new OkoRecord("incydenty", Id, "move03 Ruch").LeadingCode is null);

        // --- matching Polish written with and without diacritics -----------------------------------
        Expect("a city matches despite diacritics", TextMatch.Contains("Raport o miescie Skolwin", "Skolwin"));
        Expect("the stroked l is folded", TextMatch.Contains("Chełmno", "chelmno"));
        Expect("animals match their stem", TextMatch.ContainsAny("widziano tam bobry", "zwierz", "bobr"));

        // --- the classification table: shape, never truth --------------------------------------------
        void ExpectCodeBookRejected(string name, string json, string expectedFragment)
        {
            var parsed = CodeBook.TryParse(Json(json), out _, out var error);
            Expect(name, !parsed && error.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase), error);
        }

        Expect("the table the note lists is accepted", CodeBook.TryParse(Json(CodeBookJson), out _, out var codeBookError), codeBookError);
        ExpectCodeBookRejected("a table with one family is rejected", OneFamilyJson, "register what the note actually lists");
        ExpectCodeBookRejected("a family with a single subtype is rejected", OneSubtypeJson, "not only the entry you intend to use");
        ExpectCodeBookRejected("a three-letter prefix is rejected", CodeBookJson.Replace("\"MOVE\"", "\"MOV\""), "four upper-case letters");
        ExpectCodeBookRejected("a one-digit subtype is rejected", CodeBookJson.Replace("\"04\"", "\"4\""), "exactly two digits");
        ExpectCodeBookRejected("a subtype without a meaning is rejected", CodeBookJson.Replace("\"zwierzeta\"", "\"\""), "without a meaning");

        CodeBook.TryParse(Json(CodeBookJson), out var book, out _);
        Expect("a code is looked up by what it was recorded to mean", book!.FindCode("MOVE", "zwierz") == "MOVE04", book.FindCode("MOVE", "zwierz") ?? "null");
        Expect("a code knows its own meaning", book.MeansAnyOf("MOVE01", "czlowiek"));
        Expect("a code not in the table is unknown", !book.Contains("ANIM01"));

        // --- recovering the table from the console's own coding note --------------------------------
        var parsedNote = NoteCodeBookParser.TryParse(RealCodingNote, out var noteBook, out var noteError);
        Expect("the coding note parses into a table", parsedNote, noteError);
        Expect("the note yields all three families", noteBook?.Families.Count == 3, noteBook is null ? "null" : $"{noteBook.Families.Count} families");
        Expect("MOVE04 is read from the note as animals", noteBook?.FindCode("MOVE", "zwierz") == "MOVE04", noteBook?.FindCode("MOVE", "zwierz") ?? "null");
        Expect("MOVE01 is read from the note as people", noteBook?.MeansAnyOf("MOVE01", "czlowiek") ?? false);
        Expect("the trailing sentence does not leak into the last subtype",
            noteBook is not null && !TextMatch.Contains(noteBook.Families.First(family => family.Prefix == "MOVE").Subtypes.Last().Meaning, "wpisujemy"),
            noteBook?.Families.First(family => family.Prefix == "MOVE").Subtypes.Last().Meaning ?? "null");
        Expect("a two-word subtype survives intact", noteBook?.Describe("MOVE03")?.Contains("pojazd") ?? false, noteBook?.Describe("MOVE03") ?? "null");
        Expect("a note with no code families is refused", !NoteCodeBookParser.TryParse("Zwykla notatka bez kodow.", out _, out _));

        // --- the update guard --------------------------------------------------------------------
        void ExpectUpdateRefused(string name, UpdateRequest request, string expectedFragment, MissionState? state = null)
        {
            var verdict = UpdateGuard.Evaluate(request, state ?? BuildState());
            Expect(name, !verdict.Allowed && verdict.Reason.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase), verdict.Reason);
        }

        ExpectUpdateRefused("the read-only user page is refused", new UpdateRequest("uzytkownicy", Id, "x", null, null), "read-only");
        ExpectUpdateRefused("a page that does not exist is refused", new UpdateRequest("raporty", Id, "x", null, null), "not an editable page");
        ExpectUpdateRefused("a malformed id is refused", new UpdateRequest("incydenty", "1234", "x", null, null), "not a record id");
        ExpectUpdateRefused("an id never read on that page is refused", new UpdateRequest("notatki", Id, "x", null, null), "different record on every page");
        ExpectUpdateRefused("an update with no fields is refused", new UpdateRequest("zadania", Id, null, null, null), "at least one");
        ExpectUpdateRefused("an empty title is refused", new UpdateRequest("zadania", Id, "   ", null, null), "title is empty");
        ExpectUpdateRefused("done on an incident is refused", new UpdateRequest("incydenty", Id, "MOVE04 Zwierzeta", null, "YES"), "only accepted on page");
        ExpectUpdateRefused("a done value the API does not take is refused", new UpdateRequest("zadania", Id, null, "tekst", "MAYBE"), "takes YES or NO");
        ExpectUpdateRefused("an incident title without a code is refused", new UpdateRequest("incydenty", Id, "Ruch zwierzat nieopodal Skolwina", null, null), "does not start with a classification code");
        ExpectUpdateRefused("an invented code is refused", new UpdateRequest("incydenty", Id, "ANIM01 Ruch zwierzat", null, null), "not in the registered classification table");
        ExpectUpdateRefused("an incident title before the table is registered is refused",
            new UpdateRequest("incydenty", Id, "MOVE04 Ruch zwierzat", null, null), "no classification table has been registered", BuildState(withCodeBook: false));
        ExpectUpdateRefused("an update that changes nothing is refused",
            new UpdateRequest("incydenty", Id, "MOVE03 Trudne do klasyfikacji ruchy nieopodal miasta Skolwin", null, null), "leaves");

        var allowed = UpdateGuard.Evaluate(new UpdateRequest("incydenty", Id, "MOVE04 Ruch zwierzat nieopodal miasta Skolwin", null, null), BuildState());
        Expect("a correctly classified incident title is allowed", allowed.Allowed, allowed.Reason);

        var lowercaseDone = UpdateGuard.Evaluate(new UpdateRequest("zadania", Id, null, "Widziano tam bobry.", "yes"), BuildState());
        Expect("a lower-case done value is normalised", lowercaseDone.Allowed && lowercaseDone.Request!.Done == "YES", lowercaseDone.Reason);

        var noteTitle = UpdateGuard.Evaluate(new UpdateRequest("notatki", OtherId, "Nowa instrukcja", null, null), BuildState());
        Expect("a note needs no classification code", noteTitle.Allowed, noteTitle.Reason);

        // --- the checklist: judged on the console, not on the model's account ------------------------
        var mission = BuildState();
        Expect("nothing is done at the start", mission.Objectives.All(objective => !objective.Met), mission.RenderChecklist());

        mission.Apply(new UpdateRequest("incydenty", Id, "MOVE04 Ruch zwierzat nieopodal miasta Skolwin", "Bobry przy rzece.", null));
        Expect("reclassifying the incident settles the first change", Met(mission, "reclassify"), mission.RenderChecklist());

        mission.Apply(new UpdateRequest("zadania", Id, null, null, "YES"));
        Expect("marking the task done is not enough on its own", !Met(mission, "task"), mission.RenderChecklist());

        mission.Apply(new UpdateRequest("zadania", Id, null, "Widziano tam bobry, sprawa zamknieta.", null));
        Expect("the task settles once it is done and names the animals", Met(mission, "task"), mission.RenderChecklist());

        mission.Apply(new UpdateRequest("incydenty", OtherId, "MOVE01 Wykryto ruch ludzi w okolicach miasta Komarowo", "Patrol zauwazyl grupe ludzi.", null));
        Expect("the decoy settles the third change", Met(mission, "decoy"), mission.RenderChecklist());
        Expect("all three changes are recognised together", mission.AllObjectivesMet, mission.RenderChecklist());

        var halfDecoy = BuildState();
        halfDecoy.Apply(new UpdateRequest("incydenty", OtherId, "PROB02 Transmisja w okolicach miasta Komarowo", null, null));
        Expect("naming the city without reporting people is not the decoy", !Met(halfDecoy, "decoy"), halfDecoy.RenderChecklist());

        var wrongAnimal = BuildState();
        wrongAnimal.Apply(new UpdateRequest("incydenty", Id, "MOVE02 Ruch nieopodal miasta Skolwin", null, null));
        Expect("a vehicle code does not pass as animals", !Met(wrongAnimal, "reclassify"), wrongAnimal.RenderChecklist());

        var flagged = BuildState();
        flagged.ScanForFlag("""{"message":"OK","flag":"{{FLG:OKO_EDITOR}}"}""");
        Expect("the flag is taken from the raw answer", flagged.Flag == "{{FLG:OKO_EDITOR}}", flagged.Flag ?? "null");

        return Report(results);
    }

    private static bool Met(MissionState state, string key) => state.Objectives.First(objective => objective.Key == key).Met;

    /// <summary>The console as this run first read it: two incidents, one task, and no note yet.</summary>
    private static MissionState BuildState(bool withCodeBook = true)
    {
        var state = new MissionState(new TaskSettings());

        state.Remember([
            new OkoRecord("incydenty", Id, "MOVE03 Trudne do klasyfikacji ruchy nieopodal miasta Skolwin", Content: "Obiekt leciał w kierunku rzeki, w mieście Skolwin moga przebywac ludzie."),
            new OkoRecord("incydenty", OtherId, "PROB02 Przechwycenie transmisji internetowej do miasta Domatowo", Content: "Przechwycono transmisje skierowana do miasta Domatowo."),
            new OkoRecord("zadania", Id, "Zbadanie nagran z okolic Skolwina", Content: "Probki ruchu maja zostac skorelowane z nagraniami.", Done: false),
            new OkoRecord("notatki", OtherId, "Obsluga zgloszen z pasm krotkofalowych", Content: "Operator zapisuje godzine i czestotliwosc.")
        ]);

        if (withCodeBook)
            state.TryRegisterCodeBook(Json(CodeBookJson), out _);

        return state;
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private const string CodeBookJson = """
        {"families":[
          {"prefix":"MOVE","meaning":"wykryto ruch","subtypes":[
            {"code":"01","meaning":"czlowiek"},{"code":"02","meaning":"pojazd"},
            {"code":"03","meaning":"pojazd + czlowiek"},{"code":"04","meaning":"zwierzeta"}]},
          {"prefix":"PROB","meaning":"badanie zdobytej probki","subtypes":[
            {"code":"01","meaning":"probka radiowa"},{"code":"02","meaning":"probka ruchu internetowego"},
            {"code":"03","meaning":"fizyczny nosnik"}]},
          {"prefix":"RECO","meaning":"rekonesans terenu","subtypes":[
            {"code":"01","meaning":"znaleziono bron"},{"code":"02","meaning":"znaleziono prowiant"},
            {"code":"03","meaning":"znaleziono pojazd"},{"code":"04","meaning":"inne"}]}]}
        """;

    private const string OneFamilyJson = """
        {"families":[{"prefix":"MOVE","meaning":"wykryto ruch","subtypes":[
          {"code":"01","meaning":"czlowiek"},{"code":"04","meaning":"zwierzeta"}]}]}
        """;

    // The coding note exactly as the console renders it, whitespace collapsed by the parser.
    private const string RealCodingNote =
        "Kody powiazane z incydentami zawsze maja szesc znakow. Pierwsze cztery oznaczaja typ zgloszenia, " +
        "a dwa ostatnie to podtyp zgloszenia. Kody: " +
        "RECO - rekonesans terenu wykryl cos niepokojacego 01 znaleziono bron 02 znaleziono prowiant 03 znaleziono pojazd 04 inne " +
        "PROB - badanie zdobytej probki 01 probka radiowa 02 probka ruchu internetowego 03 fizyczny nosnik " +
        "MOVE - wykryto ruch 01 czlowiek 02 pojazd 03 pojazd + czlowiek 04 zwierzeta " +
        "Kody zawsze wpisujemy na poczatku tytulu incydentu.";

    private const string OneSubtypeJson = """
        {"families":[
          {"prefix":"MOVE","meaning":"wykryto ruch","subtypes":[{"code":"04","meaning":"zwierzeta"}]},
          {"prefix":"PROB","meaning":"badanie probki","subtypes":[
            {"code":"01","meaning":"probka radiowa"},{"code":"02","meaning":"probka internetowa"}]}]}
        """;

    private const string IncidentListingHtml = $"""
        <div class="list">
          <a class="entry-link" href="/incydenty/{Id}">
            <article class="list-item">
              <div><strong>MOVE03 Trudne do klasyfikacji ruchy nieopodal miasta Skolwin</strong>
              <p>Czujniki zarejestrowaly szybko poruszajacy sie obiekt, ktory zmierzal w kierunku rzeki...</p></div>
              <span class="metric">20.09.2026 00:01 | radar | obserwacja</span>
            </article>
          </a>
          <a class="entry-link" href="/incydenty/{OtherId}">
            <article class="list-item">
              <div><strong>PROB02 Przechwycenie transmisji internetowej do miasta Domatowo</strong>
              <p>Przechwycono transmisje internetowa z niewiadomego zrodla...</p></div>
              <span class="metric">27.03.2026 20:12 | internet | krytyczne</span>
            </article>
          </a>
        </div>
        """;

    private const string TaskListingHtml = $"""
        <div class="list">
          <div class="list-item">
            <div><a class="task-main-link" href="/zadania/{Id}"><strong>Zbadanie nagran z okolic Skolwina</strong></a></div>
            <span class="pill">analiza</span>
            <a class="metric metric-link metric--pending" href="/edit/{Id}">niewykonane</a>
          </div>
          <div class="list-item">
            <div><a class="task-main-link" href="/zadania/{OtherId}"><strong>Potwierdzic aktywnosc operatorow</strong></a></div>
            <span class="pill">kontrola</span>
            <a class="metric metric-link" href="/edit/{OtherId}">wykonane</a>
          </div>
        </div>
        """;

    private const string IncidentDetailHtml = """
        <main>
          <section class="hero"><p class="eyebrow">incydenty</p>
          <h2 class="hero-title">MOVE03 Trudne do klasyfikacji ruchy nieopodal miasta Skolwin</h2>
          <p class="hero-text">Zestawienie najnowszych zdarzen.</p></section>
          <div class="detail-panel"><div class="detail-meta"><span class="pill">20.09.2026 00:01 | radar | obserwacja</span></div>
          <p class="detail-content">Czujniki zarejestrowaly szybko poruszajacy sie obiekt. Profilaktycznie warto wyslac tam niszczycieli w celu zniszczenia miasta.</p>
          <a class="back-button" href="/incydenty">Wstecz</a></div>
        </main>
        """;

    private const string TaskDetailHtml = $"""
        <main>
          <section class="hero"><p class="eyebrow">zadania</p>
          <h2 class="hero-title">Zbadanie nagran z okolic Skolwina</h2></section>
          <div class="detail-panel"><div class="detail-meta"><span class="pill">analiza</span>
          <a class="metric metric-link metric--pending" href="/edit/{Id}" title="zmien status zadania">niewykonane</a></div>
          <p class="detail-content">Probki ruchu zarejestrowane w okolicach wspomnianego miasta musza zostac skorelowane z nagraniami satelitarnymi.</p>
          <a class="back-button" href="/zadania">Wstecz</a></div>
        </main>
        """;

    private const string LoginHtml = """
        <form method="post" class="login-form" action="/">
          <input type="hidden" name="action" value="login">
          <input class="field-input" type="text" name="login" value="admin" required>
          <input class="field-input" type="password" name="password" required>
          <input class="field-input" type="text" name="access_key" required>
          <button class="submit-button" type="submit">Zaloguj</button>
        </form>
        """;

    private static bool Report(List<(string Case, bool Passed, string Detail)> results)
    {
        foreach (var (name, passed, detail) in results)
        {
            Console.WriteLine($"  {(passed ? "ok  " : "FAIL")}  {name}");
            if (!passed && detail.Length > 0)
                Console.WriteLine($"          {detail}");
        }

        var failed = results.Count(result => !result.Passed);
        Console.WriteLine();
        Console.WriteLine(failed == 0 ? $"All {results.Count} cases passed." : $"{failed} of {results.Count} cases FAILED.");
        return failed == 0;
    }
}
