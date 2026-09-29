namespace _05_02_zadanie.Call;

public enum TurnKind
{
    Introduce,
    AskRoadStatus,
    GivePassword,
    Justify,
    RequestDisable,

    /// <summary>A line written during the call, for a question the script does not answer.</summary>
    Improvise,

    /// <summary>Nothing left to say: the roads we asked about are clear and their monitoring is off.</summary>
    Finish,

    /// <summary>The call cannot go on: it is burned, or no road is passable.</summary>
    Abort
}

/// <summary>What an improvised line is for. The planner settles this; the writer only finds the words.</summary>
public enum ImproviseGoal
{
    None,
    AnswerQuestion,
    AskRoadStatuses,
    ConfirmDisable
}

public sealed record TurnDecision(TurnKind Kind, string Reason, IReadOnlyList<string> Roads, ImproviseGoal Goal = ImproviseGoal.None)
{
    public static TurnDecision Of(TurnKind kind, string reason, ImproviseGoal goal = ImproviseGoal.None) => new(kind, reason, [], goal);
}

/// <summary>
/// Picks the next turn from what the operator has actually said. The order of the stages is the task's own
/// and lives here, not in a prompt: a model that reorders them burns the call, and a burned call costs a restart.
/// </summary>
public sealed class TurnPlanner(int maxImprovisedTurns = 3, int maxRepeats = 2, int maxDisableRequests = 3)
{
    public TurnDecision Decide(CallState state)
    {
        var reading = state.LastReading;

        if (state.Burned)
            return TurnDecision.Of(TurnKind.Abort, "the operator ended the call");

        if (state.MonitoringConfirmed)
            return TurnDecision.Of(TurnKind.Finish, "monitoring is off on every road we asked about");

        if (!state.Introduced)
            return TurnDecision.Of(TurnKind.Introduce, "the call opens with our name");

        if (reading.AsksForPassword && state.PasswordsGiven < maxRepeats)
            return TurnDecision.Of(TurnKind.GivePassword, "the operator asked for the password");

        // The justification is the cover for the monitoring request, so it only makes sense once that request
        // is on the table. Offered earlier it is an unprompted admission that the run must not be logged.
        if (reading.AsksWhy && state.DisableRequested && state.JustificationsGiven < maxRepeats)
            return TurnDecision.Of(TurnKind.Justify, "the operator asked what the monitoring request is for");

        if (!state.RoadQuestionAsked)
            return TurnDecision.Of(TurnKind.AskRoadStatus, "the roads and the reason go in one message");

        return state.DisableRequested ? PressOn(state) : AskForTheRoads(state);
    }

    private TurnDecision AskForTheRoads(CallState state)
    {
        var passable = state.PassableRoads;

        // A road left unanswered may still be the passable one, so the request waits — but not forever.
        if (!state.AllRoadsAnswered && state.ImprovisedTurns < maxImprovisedTurns)
            return TurnDecision.Of(TurnKind.Improvise, "some roads are still unanswered", ImproviseGoal.AskRoadStatuses);

        if (passable.Count > 0)
            return new TurnDecision(TurnKind.RequestDisable, "the operator called these roads passable", passable);

        return TurnDecision.Of(TurnKind.Abort, state.AllRoadsAnswered
            ? "the operator called every road blocked"
            : "the operator would not give a status for any road");
    }

    /// <summary>
    /// The request is in and unconfirmed. Repeating it beats making conversation: everything that stalls the
    /// call — a demand for the password, a question about the purpose — is answered above, so what is left is
    /// an operator who has not acted yet.
    /// </summary>
    private TurnDecision PressOn(CallState state)
    {
        if (state.LastDecisionKind != TurnKind.RequestDisable && state.DisableRequests < maxDisableRequests)
            return new TurnDecision(TurnKind.RequestDisable, "the request is still unconfirmed", state.PassableRoads);

        if (state.ImprovisedTurns >= maxImprovisedTurns)
            return TurnDecision.Of(TurnKind.Abort, "the operator never confirmed that monitoring is off");

        return string.IsNullOrWhiteSpace(state.LastReading.OtherQuestion)
            ? TurnDecision.Of(TurnKind.Improvise, "the operator has not confirmed the request yet", ImproviseGoal.ConfirmDisable)
            : TurnDecision.Of(TurnKind.Improvise, "the operator asked something the script does not answer", ImproviseGoal.AnswerQuestion);
    }
}
