using System.Text.Json;
using _02_04_zadanie.Agents;
using _02_04_zadanie.Mailbox;
using _02_04_zadanie.Mission;

namespace _02_04_zadanie.Tools;

/// <summary>
/// A read of the blackboard. The coordinator's own context already holds every report it has
/// received, but after a dozen delegations that history is long and easy to misread, and this
/// returns the same picture in one screen: current values, disagreements and the hub's verdicts.
/// </summary>
public sealed class MissionStatusTool(MissionState state, ZmailClient zmail, ResearcherRunner runner) : ITool
{
    public string Name => "mission_status";

    public string Description =>
        "Shows the shared blackboard: the current value and evidence for each of the three facts, values the " +
        "hub has already rejected, unresolved disagreements between researchers, every submission so far with " +
        "the hub's reply, and how many mailbox requests are left.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        { "type": "object", "properties": {} }
        """).RootElement;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default) =>
        Task.FromResult($"{state.RenderStatus()}\n\nresearchers launched: {runner.Launched}\n{zmail.BudgetLine}");
}
