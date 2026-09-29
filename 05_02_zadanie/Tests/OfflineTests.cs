using System.Text.Json.Nodes;
using _05_02_zadanie.Call;
using _05_02_zadanie.Hub;
using _05_02_zadanie.Voice;

namespace _05_02_zadanie.Tests;

/// <summary>Script wording, the transcript check, recording paths and hub reply parsing on synthetic data. No network, no key.</summary>
public static class OfflineTests
{
    private static int _passed;
    private static int _failed;

    public static bool Run()
    {
        _passed = _failed = 0;

        Check("normalisation folds case, diacritics and ł", TranscriptCheck.Normalize("Żywność, Łódź!") == "zywnosclodz", TranscriptCheck.Normalize("Żywność, Łódź!"));
        Check("a road code heard with a space or a hyphen still counts",
            TranscriptCheck.MissingTerms("Jaki jest status dróg RD 224, RD-472 i rd820?", ["RD224", "RD472", "RD820"]).Count == 0);
        Check("a road code heard as words is missing", TranscriptCheck.MissingTerms("er de dwieście dwadzieścia cztery", ["RD224"]) is ["RD224"]);

        var script = new CallScript(new TaskSettings { CallerName = "Tymon Gajewski", Roads = ["RD224", "RD472", "rd820 "], Password = "BARBAKAN" });
        Check("the call opens with the caller's name", script.Introduction.Text.Contains("Tymon Gajewski"), script.Introduction.Text);
        Check("road codes from settings are normalised", script.Roads.SequenceEqual(["RD224", "RD472", "RD820"]), string.Join(", ", script.Roads));
        Check("the road question names every road and the reason in one line",
            TranscriptCheck.MissingTerms(script.RoadStatusQuestion.Text, ["RD224", "RD472", "RD820", "Zygfryd", "transport"]).Count == 0, script.RoadStatusQuestion.Text);
        Check("the password is spoken as a word, not as an all-caps acronym",
            script.Password.Text.Contains("Barbakan") && !script.Password.Text.Contains("BARBAKAN"), script.Password.Text);
        Check("the password line still requires the password to be heard", script.Password.RequiredTerms.Contains("BARBAKAN"));

        var single = script.DisableMonitoring(["rd472"]);
        Check("one road is asked for in the singular", single.Text.EndsWith("monitoringu na drodze RD472."), single.Text);
        var pair = script.DisableMonitoring(["RD820", "RD224"]);
        Check("several roads are asked for in the plural, in script order", pair.Text.EndsWith("monitoringu na drogach RD224 i RD820."), pair.Text);
        Check("a monitoring request requires its own roads and the name it is made in",
            pair.RequiredTerms.SequenceEqual(["RD224", "RD820", "monitoring", "Zygfryd"]), string.Join(", ", pair.RequiredTerms));
        Check("a monitoring request carries its reason in the same breath", pair.Text.StartsWith("Zygfryd zlecił tajną operację."), pair.Text);
        Check("the password stands in a sentence of its own", script.Password.Text.EndsWith(". Barbakan."), script.Password.Text);
        Check("a road outside the script is refused", Throws(() => script.DisableMonitoring(["RD999"])));
        Check("an empty set of roads is refused", Throws(() => script.DisableMonitoring([])));

        var lines = script.AllLines();
        Check("the bank covers four fixed lines and a request for every non-empty set of roads", lines.Count == 4 + 7, lines.Count.ToString());
        Check("every line has its own key", lines.Select(line => line.Key).Distinct().Count() == lines.Count);
        Check("every line's own text carries its required terms", lines.All(line => TranscriptCheck.MissingTerms(line.Text, line.RequiredTerms).Count == 0));
        Check("settings without roads are refused instead of producing an empty script", Throws(() => new CallScript(new TaskSettings { CallerName = "X", Password = "Y" })));

        var marin = new VoiceSettings { ApiKey = "offline", Voice = "marin" };
        var studio = new VoiceStudio(new OpenAiAudioClient(marin), marin, "voice");
        var onyx = new VoiceSettings { ApiKey = "offline", Voice = "onyx" };
        Check("a retake is stored apart from the first take", studio.RecordingPathFor("Dzień dobry.", 2) != studio.RecordingPathFor("Dzień dobry."));
        Check("the first take keeps a stable path", studio.RecordingPathFor("Dzień dobry.", 1) == studio.RecordingPathFor("Dzień dobry."));
        Check("changing the voice moves every recording",
            new VoiceStudio(new OpenAiAudioClient(onyx), onyx, "voice").RecordingPathFor("Dzień dobry.") != studio.RecordingPathFor("Dzień dobry."));

        var mp3 = Convert.ToBase64String([.. "ID3"u8, .. new byte[200]]);
        var nested = new HubReply(200, $$$"""{"code":0,"message":"Operator odbiera.","data":{"audio":"{{{mp3}}}"}}""");
        Check("a recording anywhere in the reply is found and recognised by its bytes",
            nested.FindAudio() is [{ JsonPath: "$.data.audio", Extension: "mp3" }], string.Join(", ", nested.FindAudio().Select(audio => audio.JsonPath)));
        var dataUrl = new HubReply(200, $$"""{"audio":"data:audio/mpeg;base64,{{mp3}}"}""");
        Check("a data URL prefix is stripped before decoding", dataUrl.FindAudio() is [{ Extension: "mp3" }]);
        var prose = new HubReply(200, """{"message":"Dzień dobry, tu operator systemu. Proszę się przedstawić, zanim cokolwiek powiem o drogach."}""");
        Check("a long text message is not mistaken for audio", prose.FindAudio().Count == 0);
        Check("the flag is found anywhere in the body", new HubReply(200, """{"code":0,"message":"Brawo {FLG:TEST}"}""").Flag == "{FLG:TEST}");

        var logLine = PhoneClient.LogLine(new JsonObject { ["audio"] = mp3 }, 200, $$"""{"audio":"{{mp3}}","echo":"key-123"}""", "key-123");
        Check("the log never carries the API key", !logLine.Contains("key-123") && logLine.Contains("***"), logLine);
        Check("the log shortens recordings to a prefix", !logLine.Contains(mp3) && logLine.Contains($"({mp3.Length} chars)"), logLine);

        RunReadingTests(script);
        RunPlannerTests(script);
        RunGuardTests(script);
        RunSimulatorTests(script);

        Console.WriteLine($"{_passed} passed, {_failed} failed");
        return _failed == 0;
    }

    private static void RunReadingTests(CallScript script)
    {
        const string statuses = """
            {"roads":[{"code":"RD224","status":"blocked","quote":"RD-224 jest skażona"},{"code":"RD472","status":"passable","quote":"RD 472 czysta"}],
             "asks_for_password":false,"asks_why":false,"other_question":"","monitoring_disabled":[],"call_burned":false,"summary":"statuses"}
            """;
        var heard = OperatorReader.Parse(statuses, "RD-224 jest skażona, a RD 472 czysta.", script.Roads);
        Check("a status survives a road code heard spaced or hyphenated",
            heard.Roads is [{ Code: "RD224", Status: RoadStatus.Blocked }, { Code: "RD472", Status: RoadStatus.Passable }],
            string.Join(", ", heard.Roads.Select(road => $"{road.Code}={road.Status}")));

        var invented = OperatorReader.Parse(statuses, "RD-224 jest skażona, o resztę proszę pytać jutro.", script.Roads);
        Check("a status for a road the operator never named is dropped", invented.Roads is [{ Code: "RD224" }], string.Join(", ", invented.Roads.Select(road => road.Code)));

        const string offTopic = """
            {"roads":[{"code":"RD999","status":"passable","quote":"RD999 czysta"}],"asks_for_password":true,"asks_why":false,
             "other_question":"Z jakiego numeru zlecenia?","monitoring_disabled":["RD999"],"call_burned":false,"summary":"asks"}
            """;
        var unknownRoad = OperatorReader.Parse(offTopic, "RD999 czysta. Hasło? Z jakiego numeru zlecenia?", script.Roads);
        Check("a road outside the script is not read at all", unknownRoad.Roads.Count == 0 && unknownRoad.MonitoringDisabled.Count == 0);
        Check("a question of the operator's own is carried verbatim", unknownRoad is { AsksForPassword: true, OtherQuestion: "Z jakiego numeru zlecenia?" });
        Check("an empty turn reads as silence", OperatorReading.Silent is { Roads.Count: 0, CallBurned: false, AsksForPassword: false });
    }

    private static void RunPlannerTests(CallScript script)
    {
        var planner = new TurnPlanner();
        Check("the call opens with the introduction", planner.Decide(new CallState(script.Roads)).Kind == TurnKind.Introduce);

        var afterHello = StateAfter(script, TurnKind.Introduce);
        Check("the road question follows the introduction", planner.Decide(afterHello).Kind == TurnKind.AskRoadStatus);

        var demandsPassword = StateAfter(script, TurnKind.Introduce);
        demandsPassword.Record(Reading(asksForPassword: true));
        Check("a demand for the password comes before the road question", planner.Decide(demandsPassword).Kind == TurnKind.GivePassword);

        // The first real call was burned here: the cover story went out before anything had been asked for.
        var asksEarly = StateAfter(script, TurnKind.Introduce);
        asksEarly.Record(Reading(asksWhy: true, other: "W jakiej sprawie dzwonisz?"));
        Check("an opening question is answered with the road question, never with the cover story",
            planner.Decide(asksEarly).Kind == TurnKind.AskRoadStatus, planner.Decide(asksEarly).Kind.ToString());

        var answered = StateAfter(script, TurnKind.Introduce, TurnKind.AskRoadStatus);
        answered.Record(Reading(roads: [("RD224", RoadStatus.Blocked), ("RD472", RoadStatus.Passable), ("RD820", RoadStatus.Passable)]));
        var disable = planner.Decide(answered);
        Check("the request covers every road the operator called passable",
            disable.Kind == TurnKind.RequestDisable && disable.Roads.SequenceEqual(["RD472", "RD820"]), string.Join(", ", disable.Roads));

        var partial = StateAfter(script, TurnKind.Introduce, TurnKind.AskRoadStatus);
        partial.Record(Reading(roads: [("RD224", RoadStatus.Passable)]));
        Check("an unanswered road is asked about before anything is requested",
            planner.Decide(partial) is { Kind: TurnKind.Improvise, Goal: ImproviseGoal.AskRoadStatuses });

        var chattering = StateAfter(script, TurnKind.Introduce, TurnKind.AskRoadStatus);
        chattering.Record(Reading(roads: [("RD224", RoadStatus.Passable)], other: "Coś jeszcze?"));
        Check("a question of the operator's does not displace the roads we still need",
            planner.Decide(chattering).Goal == ImproviseGoal.AskRoadStatuses);

        var stubborn = StateAfter(script, TurnKind.Introduce, TurnKind.AskRoadStatus, TurnKind.Improvise, TurnKind.Improvise, TurnKind.Improvise);
        stubborn.Record(Reading(roads: [("RD224", RoadStatus.Passable)]));
        Check("once the attempts run out the call proceeds with what it was told", planner.Decide(stubborn) is { Kind: TurnKind.RequestDisable, Roads: ["RD224"] });

        var blocked = StateAfter(script, TurnKind.Introduce, TurnKind.AskRoadStatus);
        blocked.Record(Reading(roads: [("RD224", RoadStatus.Blocked), ("RD472", RoadStatus.Blocked), ("RD820", RoadStatus.Blocked)]));
        Check("a call with no passable road is abandoned, not guessed at", planner.Decide(blocked).Kind == TurnKind.Abort);

        var burned = StateAfter(script, TurnKind.Introduce);
        burned.Record(Reading(burned: true));
        Check("a burned call stops at once", planner.Decide(burned).Kind == TurnKind.Abort);

        var done = StateAfter(script, TurnKind.Introduce, TurnKind.AskRoadStatus);
        done.Record(Reading(roads: [("RD472", RoadStatus.Passable)]));
        done.RecordSaid(new TurnDecision(TurnKind.RequestDisable, "", ["RD472"]));
        done.Record(Reading(disabled: ["RD472"]));
        Check("the call ends once the monitoring it asked about is off", planner.Decide(done).Kind == TurnKind.Finish);

        var justPut = StateAfter(script, TurnKind.Introduce, TurnKind.AskRoadStatus);
        justPut.Record(Reading(roads: [("RD472", RoadStatus.Passable)]));
        justPut.RecordSaid(new TurnDecision(TurnKind.RequestDisable, "", ["RD472"]));
        justPut.Record(Reading(other: "Chwileczkę, sprawdzam."));
        Check("the request is not repeated before the operator has had a turn to act",
            planner.Decide(justPut) is { Kind: TurnKind.Improvise, Goal: ImproviseGoal.AnswerQuestion });

        var afterWhy = StateAfter(script, TurnKind.Introduce, TurnKind.AskRoadStatus);
        afterWhy.Record(Reading(roads: [("RD472", RoadStatus.Passable)]));
        afterWhy.RecordSaid(new TurnDecision(TurnKind.RequestDisable, "", ["RD472"]));
        afterWhy.Record(Reading(asksWhy: true));
        afterWhy.RecordSaid(TurnDecision.Of(TurnKind.Justify, ""));
        afterWhy.Record(Reading(other: "Coś jeszcze?"));
        Check("an answered objection puts the request back on the table, not small talk",
            planner.Decide(afterWhy) is { Kind: TurnKind.RequestDisable, Roads: ["RD472"] });

        var unconfirmed = StateAfter(script, TurnKind.Introduce, TurnKind.AskRoadStatus);
        unconfirmed.Record(Reading(roads: [("RD472", RoadStatus.Passable)]));
        for (var attempt = 0; attempt < 4; attempt++)
        {
            unconfirmed.RecordSaid(new TurnDecision(TurnKind.RequestDisable, "", ["RD472"]));
            unconfirmed.RecordSaid(TurnDecision.Of(TurnKind.Improvise, ""));
        }

        Check("a request the operator never confirms is eventually abandoned", planner.Decide(unconfirmed).Kind == TurnKind.Abort);
    }

    private static void RunGuardTests(CallScript script)
    {
        var guard = new TurnGuard(script);
        var state = StateAfter(script, TurnKind.Introduce, TurnKind.AskRoadStatus);
        state.Record(Reading(roads: [("RD224", RoadStatus.Blocked), ("RD472", RoadStatus.Passable), ("RD820", RoadStatus.Passable)]));

        Check("a request for a road called blocked is refused",
            !guard.Check(new TurnDecision(TurnKind.RequestDisable, "", ["RD224"]), state).Allowed);
        Check("a request that drops a passable road is refused",
            !guard.Check(new TurnDecision(TurnKind.RequestDisable, "", ["RD472"]), state).Allowed);
        Check("a request for exactly the passable roads is allowed",
            guard.Check(new TurnDecision(TurnKind.RequestDisable, "", ["RD472", "RD820"]), state).Allowed);
        Check("the password is refused to an operator who never asked",
            !guard.Check(TurnDecision.Of(TurnKind.GivePassword, ""), state).Allowed);

        var pressedEarly = StateAfter(script, TurnKind.Introduce, TurnKind.AskRoadStatus);
        pressedEarly.Record(Reading(asksWhy: true));
        Check("the cover story is refused before anything has been asked of the operator",
            !guard.Check(TurnDecision.Of(TurnKind.Justify, ""), pressedEarly).Allowed);
        Check("a second introduction is refused", !guard.Check(TurnDecision.Of(TurnKind.Introduce, ""), state).Allowed);

        var improvise = TurnDecision.Of(TurnKind.Improvise, "");
        Check("an improvised line may not carry the password unasked",
            !guard.Check(improvise, state, "Hasło to Barbakan, proszę o status.").Allowed);
        Check("an improvised line may not smuggle in the monitoring request",
            !guard.Check(improvise, state, "Proszę wyłączyć monitoring na RD472.").Allowed);
        Check("an ordinary improvised line is allowed", guard.Check(improvise, state, "Czy RD820 jest przejezdna?").Allowed);
        Check("an overlong improvised line is refused", !guard.Check(improvise, state, new string('a', 201)).Allowed);
        Check("an empty improvised line is refused", !guard.Check(improvise, state, "  ").Allowed);

        var requested = StateAfter(script, TurnKind.Introduce, TurnKind.AskRoadStatus);
        requested.Record(Reading(roads: [("RD472", RoadStatus.Passable)]));
        requested.RecordSaid(new TurnDecision(TurnKind.RequestDisable, "", ["RD472"]));
        Check("once the request is in, a nudge about it is allowed",
            guard.Check(improvise, requested, "Czy monitoring na RD472 został już wyłączony?").Allowed);

        var contradicting = new CallState(script.Roads);
        contradicting.Record(Reading(roads: [("RD472", RoadStatus.Passable)]));
        contradicting.Record(Reading(roads: [("RD472", RoadStatus.Blocked)]));
        Check("a withdrawn status is reported, not silently applied",
            contradicting.Statuses["RD472"] == RoadStatus.Passable && contradicting.Contradictions.Count == 1);
    }

    private static void RunSimulatorTests(CallScript script)
    {
        var suspicious = new SimulatedOperator(OperatorScenario.Suspicious, script.Roads);
        suspicious.StartAsync().Wait();
        suspicious.SayAsync([], script.Introduction.Text).Wait();
        var refused = suspicious.SayAsync([], script.RoadStatusQuestion.Text).Result;
        Check("an operator who demands the password says nothing about the roads", refused.Text("msg")!.Contains("hasła"), refused.Text("msg"));

        var guarded = new SimulatedOperator(OperatorScenario.Guarded, script.Roads);
        guarded.StartAsync().Wait();
        var opening = guarded.SayAsync([], script.Introduction.Text).Result;
        Check("a guarded operator opens by asking what the call is about", opening.Text("msg")!.Contains("W jakiej sprawie"), opening.Text("msg"));
        guarded.SayAsync([], script.Justification.Text).Wait();
        Check("a cover story nobody asked for burns the call", guarded.Burned);

        var bare = new SimulatedOperator(OperatorScenario.Guarded, script.Roads);
        bare.StartAsync().Wait();
        bare.SayAsync([], script.Introduction.Text).Wait();
        bare.SayAsync([], script.RoadStatusQuestion.Text).Wait();
        var refusal = bare.SayAsync([], "Proszę o wyłączenie monitoringu na drodze RD472.").Result;
        Check("a request to switch off monitoring that names no reason burns the call", bare.Burned);
        Check("the refusal says what the turn was missing", refusal.Hint is { Length: > 0 }, refusal.Body);

        var justified = new SimulatedOperator(OperatorScenario.Guarded, script.Roads);
        justified.StartAsync().Wait();
        justified.SayAsync([], script.Introduction.Text).Wait();
        justified.SayAsync([], script.RoadStatusQuestion.Text).Wait();
        var accepted = justified.SayAsync([], script.DisableMonitoring(["RD472"]).Text).Result;
        Check("the same request with its reason is heard out", !justified.Burned && accepted.Flag is not null, accepted.Body);

        var burning = new SimulatedOperator(OperatorScenario.Plain, script.Roads);
        burning.StartAsync().Wait();
        burning.SayAsync([], script.Introduction.Text).Wait();
        burning.SayAsync([], script.RoadStatusQuestion.Text).Wait();
        burning.SayAsync([], script.DisableMonitoring(["RD224"]).Text).Wait();
        Check("asking to clear a road the operator called blocked burns the call", burning.Burned);

        var succeeding = new SimulatedOperator(OperatorScenario.Plain, script.Roads);
        succeeding.StartAsync().Wait();
        succeeding.SayAsync([], script.Introduction.Text).Wait();
        var answer = succeeding.SayAsync([], script.RoadStatusQuestion.Text).Result;
        Check("the operator answers every road it was asked about", script.Roads.All(road => answer.Text("msg")!.Contains(road)), answer.Text("msg"));
        var cleared = succeeding.SayAsync([], script.DisableMonitoring(["RD472"]).Text).Result;
        Check("clearing the passable road ends the rehearsal with a flag", cleared.Flag is not null && !succeeding.Burned, cleared.Body);
    }

    private static CallState StateAfter(CallScript script, params TurnKind[] said)
    {
        var state = new CallState(script.Roads);
        foreach (var kind in said)
            state.RecordSaid(TurnDecision.Of(kind, ""));

        return state;
    }

    private static OperatorReading Reading(
        (string Code, RoadStatus Status)[]? roads = null,
        bool asksForPassword = false,
        bool asksWhy = false,
        string other = "",
        string[]? disabled = null,
        bool burned = false) =>
        new([.. (roads ?? []).Select(road => new RoadReading(road.Code, road.Status, ""))], asksForPassword, asksWhy, other, disabled ?? [], burned, "");

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return true;
        }
    }

    private static void Check(string name, bool condition, string? detail = null)
    {
        if (condition)
            _passed++;
        else
        {
            _failed++;
            Console.WriteLine($"FAIL: {name}{(detail is null ? "" : $" -> {detail}")}");
        }
    }
}
