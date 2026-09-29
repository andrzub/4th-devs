using _05_02_zadanie.Voice;

namespace _05_02_zadanie.Call;

public sealed record GuardVerdict(bool Allowed, string? Refusal)
{
    public static GuardVerdict Ok { get; } = new(true, null);

    public static GuardVerdict No(string refusal) => new(false, refusal);
}

/// <summary>
/// The last check before a line is spoken. It refuses what a misread turn would otherwise cost: a request to
/// clear a road nobody called passable, the password given to someone who never asked for it, or the mission's
/// own ask smuggled into an improvised line that skipped every check above.
/// </summary>
public sealed class TurnGuard(CallScript script, int maxImprovisedLength = 200)
{
    public GuardVerdict Check(TurnDecision decision, CallState state, string? improvisedText = null) => decision.Kind switch
    {
        TurnKind.Introduce when state.Introduced => GuardVerdict.No("we have already introduced ourselves"),
        TurnKind.AskRoadStatus when !state.Introduced => GuardVerdict.No("the call has to open with our name"),
        TurnKind.GivePassword when !state.LastReading.AsksForPassword => GuardVerdict.No("the operator did not ask for the password"),
        TurnKind.Justify when !state.DisableRequested => GuardVerdict.No("the cover story is for the monitoring request, which has not been made yet"),
        TurnKind.Justify when !state.LastReading.AsksWhy => GuardVerdict.No("the operator did not ask what the request is for"),
        TurnKind.RequestDisable => CheckDisable(decision, state),
        TurnKind.Improvise => CheckImprovised(improvisedText, state),
        _ => GuardVerdict.Ok
    };

    private static GuardVerdict CheckDisable(TurnDecision decision, CallState state)
    {
        if (!state.RoadQuestionAsked)
            return GuardVerdict.No("we have not asked about the roads yet");

        if (decision.Roads.Count == 0)
            return GuardVerdict.No("no road to ask about");

        var notPassable = decision.Roads.Where(road => state.Statuses.GetValueOrDefault(road) != RoadStatus.Passable).ToList();
        if (notPassable.Count > 0)
            return GuardVerdict.No($"the operator did not call {string.Join(", ", notPassable)} passable");

        var passable = state.PassableRoads;
        var missing = passable.Except(decision.Roads).ToList();
        return missing.Count > 0
            ? GuardVerdict.No($"the operator also called {string.Join(", ", missing)} passable")
            : GuardVerdict.Ok;
    }

    private GuardVerdict CheckImprovised(string? text, CallState state)
    {
        if (string.IsNullOrWhiteSpace(text))
            return GuardVerdict.No("nothing to say");

        if (text.Length > maxImprovisedLength)
            return GuardVerdict.No($"{text.Length} characters is past the {maxImprovisedLength} we keep a turn to");

        var normalized = TranscriptCheck.Normalize(text);
        if (!state.LastReading.AsksForPassword && normalized.Contains(TranscriptCheck.Normalize(script.PasswordWord), StringComparison.Ordinal))
            return GuardVerdict.No("the password belongs in a turn the operator asked for");

        // The mission's own ask carries the road checks above; an improvised line must not carry it instead.
        var asksToDisable = normalized.Contains("monitoring", StringComparison.Ordinal)
            && (normalized.Contains("wylacz", StringComparison.Ordinal) || normalized.Contains("odblok", StringComparison.Ordinal));
        return asksToDisable && !state.DisableRequested
            ? GuardVerdict.No("asking to disable monitoring is a turn of its own, after the roads are known")
            : GuardVerdict.Ok;
    }
}
