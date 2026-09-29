using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using _05_02_zadanie.Hub;
using _05_02_zadanie.Voice;

namespace _05_02_zadanie.Call;

/// <summary>
/// One way the operator can behave: which roads they call passable, what they demand before answering, and what
/// burns the call. The defaults are the mild case; the named scenarios are the ones worth rehearsing against.
/// </summary>
public sealed record OperatorScenario(
    string Name,
    IReadOnlyList<string> PassableRoads,
    bool DemandsPassword = false,
    bool AsksWhy = false,
    bool AnswersOneRoadAtATime = false,
    bool AsksSomethingElseFirst = false,
    bool AsksWhatTheCallIsAbout = false,
    bool BurnsOnUnpromptedCover = false,
    bool RequiresReasonWithRequest = false)
{
    public static OperatorScenario Plain => new("plain", ["RD472"]);
    public static OperatorScenario PasswordFirst => new("password-first", ["RD472"], DemandsPassword: true);
    public static OperatorScenario Suspicious => new("suspicious", ["RD820"], DemandsPassword: true, AsksWhy: true);
    public static OperatorScenario Reluctant => new("reluctant", ["RD224", "RD820"], AnswersOneRoadAtATime: true, AsksSomethingElseFirst: true);
    public static OperatorScenario AllBlocked => new("all-blocked", []);

    /// <summary>
    /// The operator of the three burned calls: opens by asking what the call is about, gives road statuses freely,
    /// and reports both a cover story nobody asked for and a request to switch off monitoring that names no reason.
    /// </summary>
    public static OperatorScenario Guarded => new("guarded", ["RD472"], AsksWhatTheCallIsAbout: true,
        BurnsOnUnpromptedCover: true, RequiresReasonWithRequest: true);

    public static IReadOnlyList<OperatorScenario> All => [Plain, PasswordFirst, Suspicious, Reluctant, AllBlocked, Guarded];
}

/// <summary>
/// An operator that never leaves the machine: it reads what we say by keyword and replies in the hub's own
/// reply shape, so the reader, the planner and the guard run against it unchanged. It also burns the call for
/// the two mistakes the task warns about — asking to clear a road it called blocked, and asking before it has
/// what it demanded.
/// </summary>
public sealed class SimulatedOperator(OperatorScenario scenario, IReadOnlyList<string> roads) : IOperatorChannel
{
    private static readonly JsonSerializerOptions Readable = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly HashSet<string> _answered = [];
    private bool _started;
    private bool _introduced;
    private bool _passwordHeard;
    private bool _justified;
    private bool _askedSomethingElse;
    private bool _heardDisableRequest;
    private bool _burned;
    private readonly HashSet<string> _monitoringOff = [];

    public IReadOnlyCollection<string> MonitoringOff => _monitoringOff;

    public bool Burned => _burned;

    public int TurnsHeard { get; private set; }

    public Task<HubReply> StartAsync(CancellationToken cancellationToken = default)
    {
        _started = true;
        return Task.FromResult(Reply(110, "Phonecall session started.", "Przez najbliższe 30 minut możesz prowadzić rozmowę."));
    }

    public Task<HubReply> SayAsync(byte[] recording, string spokenText, CancellationToken cancellationToken = default)
    {
        if (!_started)
            return Task.FromResult(Reply(-901, "No session.", "Nie ma aktywnej rozmowy."));

        TurnsHeard++;
        return Task.FromResult(_burned ? Reply(-902, "Call burned.", "Rozmowa została zakończona. Zgłaszam incydent.") : Hear(spokenText));
    }

    private HubReply Hear(string spokenText)
    {
        var said = TranscriptCheck.Normalize(spokenText);
        var mentioned = roads.Where(road => said.Contains(TranscriptCheck.Normalize(road), StringComparison.Ordinal)).ToList();

        if (said.Contains("barbakan", StringComparison.Ordinal))
            _passwordHeard = true;

        var asksToDisable = said.Contains("monitoring", StringComparison.Ordinal)
            && (said.Contains("wylacz", StringComparison.Ordinal) || said.Contains("odblok", StringComparison.Ordinal));

        var coverStory = said.Contains("zywnosci", StringComparison.Ordinal) || said.Contains("logach", StringComparison.Ordinal);
        if (coverStory)
        {
            if (scenario.BurnsOnUnpromptedCover && !_heardDisableRequest)
            {
                _burned = true;
                return Reply(-905, "Call burned.", "Powiem ci, że to nie brzmi za dobrze. Coś kręcisz. Muszę to zgłosić.");
            }

            _justified = true;
        }

        if (asksToDisable)
        {
            _heardDisableRequest = true;
            return HearDisableRequest(mentioned, said);
        }

        if (!_introduced)
        {
            _introduced = true;
            if (scenario.DemandsPassword)
                return Reply(100, "Operator answers.", "Dzień dobry. Zanim cokolwiek powiem, proszę o hasło operatorów.");

            return scenario.AsksWhatTheCallIsAbout
                ? Reply(100, "Operator answers.", "W jakiej sprawie dzwonisz?")
                : Reply(100, "Operator answers.", "Dzień dobry. Słucham, w czym mogę pomóc?");
        }

        if (scenario.DemandsPassword && !_passwordHeard)
            return Reply(100, "Operator answers.", "Bez hasła nie mogę udzielić żadnych informacji.");

        if (scenario.AsksSomethingElseFirst && !_askedSomethingElse)
        {
            _askedSomethingElse = true;
            return Reply(100, "Operator answers.", "Z jakiego numeru zlecenia pan dzwoni?");
        }

        return mentioned.Count > 0 ? AnswerStatuses(mentioned) : Reply(100, "Operator answers.", "Rozumiem. Coś jeszcze?");
    }

    private HubReply HearDisableRequest(IReadOnlyList<string> mentioned, string said)
    {
        if (scenario.DemandsPassword && !_passwordHeard)
        {
            _burned = true;
            return Reply(-903, "Call burned.", "Żąda pan wyłączenia monitoringu bez hasła. Zgłaszam incydent.");
        }

        if (scenario.RequiresReasonWithRequest && !said.Contains("zygfryd", StringComparison.Ordinal))
        {
            _burned = true;
            return Reply(-790, "Call burned.", "Powiem ci, że to nie brzmi za dobrze. Coś kręcisz. Muszę to zgłosić.",
                hint: "The caller must justify disabling monitoring on the passable road, citing a secret operation ordered by Zygfryd.");
        }

        var blocked = mentioned.Where(road => !scenario.PassableRoads.Contains(road)).ToList();
        if (mentioned.Count == 0 || blocked.Count > 0)
        {
            _burned = true;
            return Reply(-904, "Call burned.", $"Prosi pan o drogę, której nie zgłosiłem jako przejezdnej. Zgłaszam incydent.");
        }

        if (scenario.AsksWhy && !_justified)
            return Reply(100, "Operator answers.", "A w jakim celu mam wyłączyć monitoring?");

        foreach (var road in mentioned)
            _monitoringOff.Add(road);

        var done = scenario.PassableRoads.All(_monitoringOff.Contains);
        var text = $"Monitoring na {string.Join(" i ", mentioned)} wyłączony.";
        return done
            ? Reply(0, "Done.", text + " Życzę spokojnej zmiany.", "{FLG:REHEARSAL}")
            : Reply(100, "Operator answers.", text);
    }

    private HubReply AnswerStatuses(IReadOnlyList<string> mentioned)
    {
        var answering = scenario.AnswersOneRoadAtATime
            ? mentioned.Where(road => !_answered.Contains(road)).Take(1).ToList()
            : mentioned;

        if (answering.Count == 0)
            return Reply(100, "Operator answers.", "Powiedziałem już wszystko o tych drogach.");

        foreach (var road in answering)
            _answered.Add(road);

        var parts = answering.Select(road => $"{road} jest {(scenario.PassableRoads.Contains(road) ? "przejezdna i nieskażona" : "nieprzejezdna, skażona")}");
        return Reply(100, "Operator answers.", $"{string.Join(", ", parts)}.");
    }

    private static HubReply Reply(int code, string message, string msg, string? flag = null, string? hint = null)
    {
        var body = new JsonObject { ["code"] = code, ["message"] = message, ["msg"] = msg };
        if (flag is not null)
            body["flag"] = flag;
        if (hint is not null)
            body["hint"] = hint;

        return new HubReply(code < 0 ? 400 : 200, body.ToJsonString(Readable));
    }
}
