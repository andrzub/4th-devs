using _02_04_zadanie.Mission;

namespace _02_04_zadanie.Agents;

/// <summary>
/// What the coordinator hands to one researcher. A researcher never sees the coordinator's
/// conversation, so everything it needs has to be in here.
/// </summary>
/// <param name="Fact">
/// The blackboard slot the answer fills, or null for a reconnaissance assignment that only
/// reports what is in the mailbox.
/// </param>
/// <param name="ExcludeValues">Values already known to be wrong, enforced in code at report time.</param>
public sealed record ResearchAssignment(MissionFact? Fact, string Briefing, IReadOnlyList<string> ExcludeValues);

/// <summary>The researcher's single, structured answer, produced by its report_finding call.</summary>
public sealed record ResearchReport(bool Found, string? Value, string? Evidence, string? MessageId, string? Notes)
{
    public FindingOutcome? Outcome { get; set; }
}

public sealed record ResearcherResult(string Label, ResearchReport? Report, AgentRunResult Run);
