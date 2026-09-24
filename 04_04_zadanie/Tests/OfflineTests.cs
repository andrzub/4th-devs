using _04_04_zadanie.Agents;
using _04_04_zadanie.Filesystem;
using _04_04_zadanie.Llm;
using _04_04_zadanie.Mission;
using _04_04_zadanie.Notes;
using _04_04_zadanie.Tools;

namespace _04_04_zadanie.Tests;

/// <summary>
/// The rules that stand between the agent and the hub, checked offline on a made-up trade world so
/// that the checks are exercised without the real answer being written into code. Every case is a
/// way a submission could have failed verification.
/// </summary>
public static class OfflineTests
{
    private const string Announcements = """
        Komarowo: 3 koparki, 40 workow cementu, pilne.
        Skolwin prosi o 12 desek i 5 koparek.
        W Zarnowcu potrzeba 7 cegiel.
        """;

    private const string Diary = """
        - z Komarowa dzwonil Jan Kowalski, pyta o koparki.
        - Skolwin: Nowak ma oddzwonic w sprawie desek. Anna potem potwierdzila, ze pilnuje tam handlu.
        - Zarnowiec: Piotr Zielinski, temat cegiel.
        """;

    private const string Ledger = """
        Komarowo -> deska -> Skolwin
        Skolwin -> cegła -> Zarnowiec
        Zarnowiec -> koparka -> Komarowo
        Skolwin -> ziemniaki -> Komarowo
        """;

    public static bool Run()
    {
        var results = new List<(string Case, bool Passed, string Detail)>();

        void Expect(string name, bool passed, string detail = "") => results.Add((name, passed, detail));

        void ExpectNormalized(string name, string path, string expected)
        {
            try
            {
                var actual = VirtualFilesystem.NormalizePath(path);
                Expect(name, actual == expected, actual);
            }
            catch (ArgumentException ex)
            {
                Expect(name, false, ex.Message);
            }
        }

        void ExpectInvalidPath(string name, string path, string fragment)
        {
            try
            {
                VirtualFilesystem.NormalizePath(path);
                Expect(name, false, "accepted");
            }
            catch (ArgumentException ex)
            {
                Expect(name, ex.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase), ex.Message);
            }
        }

        void ExpectAllowed(string name, VirtualFilesystem filesystem, string path, string content)
        {
            var verdict = WriteGuard.Evaluate(filesystem, path, content);
            Expect(name, verdict.Allowed, verdict.Reason);
        }

        void ExpectRefused(string name, VirtualFilesystem filesystem, string path, string content, string fragment)
        {
            var verdict = WriteGuard.Evaluate(filesystem, path, content);
            Expect(name, !verdict.Allowed && verdict.Reason.Contains(fragment, StringComparison.OrdinalIgnoreCase), verdict.Allowed ? "allowed" : verdict.Reason);
        }

        // --- paths ------------------------------------------------------------------------------------
        ExpectNormalized("an absolute path is kept", "/miasta/puck", "/miasta/puck");
        ExpectNormalized("duplicate slashes are collapsed", "/miasta//puck", "/miasta/puck");
        ExpectNormalized("a trailing slash is dropped", "/miasta/puck/", "/miasta/puck");
        ExpectNormalized("surrounding whitespace is trimmed", "  /miasta/puck ", "/miasta/puck");
        ExpectInvalidPath("a relative path is refused", "miasta/puck", "absolute");
        ExpectInvalidPath("walking up the tree is refused", "/miasta/../osoby/x", "walks");
        ExpectInvalidPath("an empty path is refused", "", "empty");
        ExpectInvalidPath("inner whitespace is refused", "/miasta/nowy targ", "whitespace");

        // --- the projection ---------------------------------------------------------------------------
        var filesystem = Bootstrap();
        Expect("the three directories exist", FilesystemLayout.Directories.All(filesystem.DirectoryExists));
        Expect("the root lists the directories", filesystem.List("/").SequenceEqual(["miasta/", "osoby/", "towary/"]), string.Join(" ", filesystem.List("/")));
        Expect("a first write is not a replacement", !filesystem.WriteFile("/miasta/komarowo", "{}"));
        Expect("a second write is a replacement", filesystem.WriteFile("/miasta/komarowo", "{\"koparka\": 3}"));
        Expect("the file reads back", filesystem.ReadFile("/miasta/komarowo") == "{\"koparka\": 3}");
        Expect("a name is found wherever it sits", filesystem.PathsNamed("komarowo").SequenceEqual(["/miasta/komarowo"]) && filesystem.PathsNamed("miasta").SequenceEqual(["/miasta"]));
        Expect("writing into a missing directory fails", Throws(() => filesystem.WriteFile("/inne/x", "x")));
        Expect("deleting a file removes one entry", filesystem.Delete("/miasta/komarowo") == 1 && !filesystem.FileExists("/miasta/komarowo"));
        Expect("deleting a missing path fails", Throws(() => filesystem.Delete("/miasta/komarowo")));
        var roundTrip = VirtualFilesystem.FromJson(Reference().ToJson());
        Expect("the plan survives a JSON round trip", roundTrip.ToJson() == Reference().ToJson());

        // --- the guard: names and placement -----------------------------------------------------------
        var world = Reference();
        ExpectAllowed("a city file with JSON needs is allowed", world, "/miasta/komarowo", "{\"koparka\": 3, \"cement\": 40}");
        ExpectAllowed("a city with no needs is allowed", world, "/miasta/nowe", "{}");
        ExpectAllowed("a multi-word city name uses an underscore", world, "/miasta/nowa_wies", "{}");
        ExpectRefused("a Polish letter in a city name is refused", world, "/miasta/żarnowiec", "{}", "non-ASCII");
        ExpectRefused("a capitalised city name is refused", world, "/miasta/Komarowo", "{}", "lowercase");
        ExpectRefused("a hyphen in a name is refused", world, "/miasta/nowa-wies", "{}", "lowercase");
        ExpectRefused("an extension is refused", world, "/miasta/komarowo.json", "{}", "extension");
        ExpectRefused("a name over twenty characters is refused", world, "/miasta/" + new string('a', 21), "{}", "at most 20");
        ExpectRefused("a name already used in another directory is refused", world, "/towary/komarowo", "[Komarowo](/miasta/komarowo)", "unique across");
        ExpectRefused("a name equal to a directory's is refused", world, "/towary/miasta", "[Komarowo](/miasta/komarowo)", "unique across");
        ExpectRefused("a file at the root is refused", world, "/komarowo", "{}", "directly into");
        ExpectRefused("a subdirectory is refused", world, "/miasta/polnoc/komarowo", "{}", "directly into");
        ExpectRefused("an unknown directory is refused", world, "/handel/komarowo", "{}", "directly into");
        ExpectRefused("empty content is refused", world, "/miasta/komarowo", "  ", "empty");

        // --- the guard: city content ------------------------------------------------------------------
        ExpectRefused("a city with non-JSON content is refused", world, "/miasta/komarowo", "koparka: 3", "not valid JSON");
        ExpectRefused("a fenced JSON block is refused", world, "/miasta/komarowo", "```json\n{\"koparka\": 3}\n```", "not valid JSON");
        ExpectRefused("a JSON array is refused", world, "/miasta/komarowo", "[3]", "object");
        ExpectRefused("a quantity with a unit is refused", world, "/miasta/komarowo", "{\"cement\": \"40 workow\"}", "positive integer");
        ExpectRefused("a zero quantity is refused", world, "/miasta/komarowo", "{\"cement\": 0}", "positive integer");
        ExpectRefused("a fractional quantity is refused", world, "/miasta/komarowo", "{\"cement\": 4.5}", "positive integer");
        ExpectRefused("a capitalised key is refused", world, "/miasta/komarowo", "{\"Cement\": 40}", "lowercase");
        ExpectRefused("a key with a space is refused", world, "/miasta/komarowo", "{\"worek cementu\": 40}", "lowercase");
        ExpectRefused("a Polish letter in a key is refused", world, "/miasta/komarowo", "{\"cegła\": 7}", "non-ASCII");
        ExpectRefused("a plural key is refused", world, "/miasta/komarowo", "{\"koparki\": 3}", "plural");
        ExpectRefused("a plural key in -y is refused", world, "/miasta/komarowo", "{\"lopaty\": 9}", "plural");

        // --- the guard: people ------------------------------------------------------------------------
        ExpectAllowed("a person with a name and a city link is allowed", world, "/osoby/jan_kowalski", "Jan Kowalski\nOdpowiada za handel w miescie [Komarowo](/miasta/komarowo).");
        ExpectRefused("a person file without an underscore name is refused", world, "/osoby/kowalski", "Jan Kowalski [Komarowo](/miasta/komarowo)", "firstname_surname");
        ExpectRefused("a surname alone is sent back to the diary", world, "/osoby/kowalski", "Kowalski [Komarowo](/miasta/komarowo)", "diary");
        ExpectRefused("a name that repeats a word is refused", world, "/osoby/kowalski_kowalski", "Kowalski Kowalski [Komarowo](/miasta/komarowo)", "repeats");
        ExpectRefused("a capitalised person file name is refused", world, "/osoby/Jan_Kowalski", "Jan Kowalski [Komarowo](/miasta/komarowo)", "lowercase");
        ExpectRefused("a person without a full name is refused", world, "/osoby/jan_kowalski", "Kowalski, [Komarowo](/miasta/komarowo)", "full name");
        ExpectRefused("a person without a link is refused", world, "/osoby/jan_kowalski", "Jan Kowalski, Komarowo", "exactly one markdown link");
        ExpectRefused("a person with two links is refused", world, "/osoby/jan_kowalski", "Jan Kowalski [Komarowo](/miasta/komarowo) [Skolwin](/miasta/skolwin)", "exactly one");
        ExpectRefused("a link to a missing city is refused", world, "/osoby/jan_kowalski", "Jan Kowalski [Puck](/miasta/puck)", "does not exist");
        ExpectRefused("a relative link is refused", world, "/osoby/jan_kowalski", "Jan Kowalski [Komarowo](komarowo)", "absolute");
        ExpectRefused("a link outside /miasta is refused", world, "/osoby/jan_kowalski", "Jan Kowalski [Komarowo](/towary/deska)", "does not point into");
        ExpectRefused("a Polish letter in the text is refused", world, "/osoby/jan_kowalski", "Jan Kowalski zarządza [Komarowo](/miasta/komarowo)", "non-ASCII");

        // --- the guard: goods -------------------------------------------------------------------------
        ExpectAllowed("a good with seller links is allowed", world, "/towary/deska", "Oferuje: [Komarowo](/miasta/komarowo)\nOferuje: [Skolwin](/miasta/skolwin)");
        ExpectRefused("a capitalised good name is refused", world, "/towary/Deska", "[Komarowo](/miasta/komarowo)", "lowercase");
        ExpectRefused("a plural good name is refused even when the ledger spells it so", world, "/towary/ziemniaki", "[Skolwin](/miasta/skolwin)", "plural");
        ExpectRefused("a good without links is refused", world, "/towary/deska", "Komarowo sprzedaje", "no markdown link");
        ExpectRefused("a good linking the same city twice is refused", world, "/towary/deska", "[Komarowo](/miasta/komarowo) [Komarowo](/miasta/komarowo/)", "more than once");
        ExpectRefused("a good linking a person is refused", world, "/towary/deska", "[Jan](/osoby/jan_kowalski)", "does not point into");

        // --- folding and the ledger -------------------------------------------------------------------
        Expect("Polish letters fold to base letters", PolishText.Fold("Wołowina łopata mąka ryż Żarnowiec") == "Wolowina lopata maka ryz Zarnowiec", PolishText.Fold("Wołowina łopata mąka ryż Żarnowiec"));
        var ledger = TransactionParser.Parse(Ledger);
        Expect("the ledger parses every line", ledger.Count == 4 && ledger[1] == new Transaction("Skolwin", "cegła", "Zarnowiec"), $"{ledger.Count} lines");
        Expect("a malformed ledger line is rejected", Throws(() => TransactionParser.Parse("Skolwin -> cegła")));
        Expect("an exact good name matches the ledger", PlanValidator.MatchGood("deska", ["deska", "deski"]).SequenceEqual(["deska"]));
        Expect("a plural ledger item matches its singular file", PlanValidator.MatchGood("ziemniaki", ["ziemniak", "cement"]).SequenceEqual(["ziemniak"]));
        Expect("a short stem does not match a longer word", PlanValidator.MatchGood("maka", ["makaron"]).Count == 0);
        Expect("an unrelated name does not match", PlanValidator.MatchGood("cegla", ["deska"]).Count == 0);

        // --- the validator ----------------------------------------------------------------------------
        var sources = new ValidationSources(Announcements, Diary, ledger);
        Expect("the reference world is valid", PlanValidator.Validate(Reference(), sources).IsValid, PlanValidator.Validate(Reference(), sources).Render());
        Expect("a city appears in the notes by stem", PlanValidator.AppearsInNotes("zarnowiec", sources) && PlanValidator.AppearsInNotes("komarowo", sources));
        Expect("an invented city does not appear in the notes", !PlanValidator.AppearsInNotes("gdynia", sources));

        ExpectInvalid("a city without a person", Mutate(reference => reference.Delete("/osoby/piotr_zielinski")), sources, "no person");
        ExpectInvalid("two people for one city", Mutate(reference => reference.WriteFile("/osoby/ewa_nowak", "Ewa Nowak [Skolwin](/miasta/skolwin)")), sources, "2 people");
        ExpectInvalid("a good sold in the ledger without a file", Mutate(reference => reference.Delete("/towary/cegla")), sources, "no file");
        ExpectInvalid("a good linking a city that never sells it", Mutate(reference => reference.WriteFile("/towary/deska", "[Komarowo](/miasta/komarowo)\n[Zarnowiec](/miasta/zarnowiec)")), sources, "never sells");
        ExpectInvalid("a good missing one of its sellers", Mutate(reference => reference.WriteFile("/towary/koparka", "[Skolwin](/miasta/skolwin)")), sources, "not linked");
        ExpectInvalid("a good nobody sells", Mutate(reference => reference.WriteFile("/towary/cement", "[Komarowo](/miasta/komarowo)")), sources, "no transaction");
        ExpectInvalid("a quantity not on the board", Mutate(reference => reference.WriteFile("/miasta/komarowo", "{\"koparka\": 30}")), sources, "appears nowhere");
        ExpectInvalid("an invented person", Mutate(reference => { reference.Delete("/osoby/jan_kowalski"); reference.WriteFile("/osoby/adam_wisniewski", "Adam Wisniewski [Komarowo](/miasta/komarowo)"); }), sources, "does not appear");
        ExpectInvalid("an invented city", Mutate(reference => reference.WriteFile("/miasta/gdynia", "{\"cement\": 40}")), sources, "does not appear");
        ExpectInvalid("a city missing from the ledger's parties", Mutate(reference => { reference.Delete("/miasta/zarnowiec"); reference.Delete("/osoby/piotr_zielinski"); reference.Delete("/towary/koparka"); }), sources, "trades but has no file");
        ExpectInvalid("a stray root file", Mutate(reference => reference.WriteFile("/readme", "x")), sources, "does not belong");
        ExpectInvalid("a person left pointing at a deleted city", Mutate(reference => reference.Delete("/miasta/zarnowiec")), sources, "does not exist");

        // --- the batch --------------------------------------------------------------------------------
        var batch = BatchBuilder.Build(Reference(), ApiVocabulary.Default);
        Expect("the batch starts with the three directories", batch.Count == 3 + Reference().Files.Count && batch.Take(3).All(action => action!["action"]!.GetValue<string>() == "createDirectory"), $"{batch.Count} actions");
        Expect("the batch carries every file with its content", batch.Skip(3).All(action => action!["action"]!.GetValue<string>() == "createFile" && action["content"] is not null));
        Expect("directories come before their files", batch[0]!["path"]!.GetValue<string>() == "/miasta" && batch[3]!["path"]!.GetValue<string>().StartsWith("/miasta/"));

        void ExpectInvalid(string name, VirtualFilesystem mutated, ValidationSources validationSources, string fragment)
        {
            var report = PlanValidator.Validate(mutated, validationSources);
            Expect(name, !report.IsValid && report.Findings.Any(finding => finding.Severity == Severity.Error && finding.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase)), report.Render());
        }

        // --- the hooks, on the real notes and workspace shipped next to the binary --------------------
        var notes = new NoteLibrary(Path.Combine(AppContext.BaseDirectory, "natan-notes"));
        var workspace = new Workspace(Path.Combine(AppContext.BaseDirectory, "workspace"));
        var state = new FilingState(notes, workspace);
        var hooks = new FilingHooks(state);
        var write = new ToolCall { Id = "1", FunctionName = WriteFileTool.ToolName, ArgumentsJson = "{\"path\":\"/miasta/nowe\",\"content\":\"{}\"}" };

        string? Before(ToolCall call) => hooks.BeforeToolCallAsync(call).GetAwaiter().GetResult();
        string Execute(ITool tool, string arguments) => tool.ExecuteAsync(arguments).GetAwaiter().GetResult();

        Expect("a write before the notes are read is refused", Before(write)?.Contains("read every note", StringComparison.OrdinalIgnoreCase) == true, Before(write) ?? "allowed");
        var readNote = new ReadNoteTool(notes, state);
        var folded = Execute(readNote, "{\"name\":\"ogloszenia.txt\"}");
        Expect("a note asked for in plain ASCII is still found", folded.Contains("TABLICA OGLOSZEN") && state.NotesRead.Contains("ogłoszenia.txt"), folded[..Math.Min(80, folded.Length)]);
        foreach (var note in notes.Names)
            Execute(readNote, $"{{\"name\":\"{note}\"}}");
        Expect("every note counts as read", state.AllNotesRead, string.Join(", ", state.UnreadNotes));
        Expect("a write before the template is read is refused", Before(write)?.Contains("template", StringComparison.OrdinalIgnoreCase) == true, Before(write) ?? "allowed");
        Execute(new ReadTemplateTool(workspace, state), "{\"name\":\"miasto.md\"}");
        Expect("a write after the template is read passes the hook", Before(write) is null, Before(write) ?? "allowed");
        Expect("a write into another directory still needs its own template", Before(new ToolCall { Id = "2", FunctionName = WriteFileTool.ToolName, ArgumentsJson = "{\"path\":\"/towary/x\",\"content\":\"x\"}" })?.Contains("towar.md") == true);
        Expect("finishing on an empty plan is turned back", hooks.BeforeFinishAsync().GetAwaiter().GetResult()?.Contains("not complete") == true);
        Expect("an empty plan is not accepted", !state.Accepted && state.LastReport is { IsValid: false });
        var written = Execute(new WriteFileTool(state), "{\"path\":\"/miasta/mechowo\",\"content\":\"{}\"}");
        Expect("the write tool files through the guard", written.StartsWith("Written /miasta/mechowo") && state.Writes == 1, written);
        Expect("progress is counted by code", state.RenderProgress().Contains("miasta: 1") && state.RenderProgress().Contains("notes read: 4/4"), state.RenderProgress());

        return Report(results);
    }

    public static VirtualFilesystem Bootstrap()
    {
        var filesystem = new VirtualFilesystem();
        foreach (var directory in FilesystemLayout.Directories)
            filesystem.CreateDirectory(directory);
        return filesystem;
    }

    /// <summary>The correct filing of the made-up world above.</summary>
    private static VirtualFilesystem Reference()
    {
        var filesystem = Bootstrap();
        filesystem.WriteFile("/miasta/komarowo", "{\"koparka\": 3, \"cement\": 40}");
        filesystem.WriteFile("/miasta/skolwin", "{\"deska\": 12, \"koparka\": 5}");
        filesystem.WriteFile("/miasta/zarnowiec", "{\"cegla\": 7}");
        filesystem.WriteFile("/osoby/jan_kowalski", "Jan Kowalski\nOdpowiada za handel w miescie [Komarowo](/miasta/komarowo).");
        filesystem.WriteFile("/osoby/anna_nowak", "Anna Nowak\nOdpowiada za handel w miescie [Skolwin](/miasta/skolwin).");
        filesystem.WriteFile("/osoby/piotr_zielinski", "Piotr Zielinski\nOdpowiada za handel w miescie [Zarnowiec](/miasta/zarnowiec).");
        filesystem.WriteFile("/towary/deska", "Oferuje: [Komarowo](/miasta/komarowo)");
        filesystem.WriteFile("/towary/cegla", "Oferuje: [Skolwin](/miasta/skolwin)");
        filesystem.WriteFile("/towary/koparka", "Oferuje: [Zarnowiec](/miasta/zarnowiec)");
        filesystem.WriteFile("/towary/ziemniak", "Oferuje: [Skolwin](/miasta/skolwin)");
        return filesystem;
    }

    private static VirtualFilesystem Mutate(Action<VirtualFilesystem> change)
    {
        var filesystem = Reference();
        change(filesystem);
        return filesystem;
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception)
        {
            return true;
        }
    }

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
