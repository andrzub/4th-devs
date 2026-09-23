using System.Text.Json.Nodes;
using _04_03_zadanie.City;
using _04_03_zadanie.Hub;

namespace _04_03_zadanie.Mission;

public enum StepOutcome
{
    /// <summary>An action was sent and accepted.</summary>
    Sent,

    /// <summary>The next action was chosen and shown, nothing sent (--next).</summary>
    Planned,

    /// <summary>The human is confirmed; the helicopter call is left to the operator.</summary>
    Finished,

    /// <summary>The guard refused the chosen action; nothing sent.</summary>
    Rejected,

    /// <summary>The hub refused or failed the action.</summary>
    Failed,

    /// <summary>The tactician found nothing sensible to do.</summary>
    Stuck
}

/// <summary>
/// Runs the search one action at a time: observe the board through the free reads, let the tactician
/// choose, let the guard price and vet, send, and observe again. Nothing is remembered between steps
/// except the ledger of who is still aboard, so a run can stop after any step and resume later, or be
/// driven by hand with --step. The helicopter is never called from here.
/// </summary>
public sealed class Operation(DomatowoClient hub, CityMap map, CostTable costs, TaskSettings settings, OperationLedger ledger, IReadOnlyList<Cluster> clusters, ReplyArchive archive, Action<string> say)
{
    private readonly ActionGuard _guard = new(map, costs);

    /// <summary>
    /// Points and flags from the preview backend, positions from getObjects, fresh log entries from
    /// getLogs, and everything the ledger remembers on top. Three free reads.
    /// </summary>
    public async Task<OperationState> ObserveAsync()
    {
        var state = settings.NewOperationState();

        var pull = await hub.PullStateAsync();
        state.ApplyStats(pull.TryParseJson() ?? throw new InvalidOperationException($"State read failed: HTTP {pull.Status} {pull.Body}"));

        var objects = await hub.GetObjectsAsync();
        if (!objects.IsOk)
            throw new InvalidOperationException($"getObjects failed: HTTP {objects.Status} {objects.Body}");
        state.ApplyObjects(objects.TryParseJson()!);

        var logs = await hub.GetLogsAsync();
        if (!logs.IsOk)
            throw new InvalidOperationException($"getLogs failed: HTTP {logs.Status} {logs.Body}");
        state.ApplyLogs(logs.TryParseJson()!);

        ledger.Resolve(state);
        return state;
    }

    public async Task<StepOutcome> StepAsync(bool send)
    {
        var state = await ObserveAsync();
        Describe(state);

        var decision = Tactician.Decide(state, map, clusters, costs);
        switch (decision)
        {
            case Decision.Finished finished:
                say($"decision: FINISHED - {finished.Why}");
                say(string.Empty);
                say("The helicopter is the operator's call. Run:");
                say($"  dotnet run -- --action callHelicopter destination={finished.HumanAt}");
                return StepOutcome.Finished;

            case Decision.Stuck stuck:
                say($"decision: STUCK - {stuck.Reason}");
                return StepOutcome.Stuck;

            case Decision.Act act:
                say($"decision: {act.Summary}");
                say($"  why: {act.Why}");

                var verdict = _guard.Check(act.Answer, state);
                say($"guard: {verdict}");
                if (!verdict.Accepted)
                    return StepOutcome.Rejected;

                if (!send)
                {
                    say($"would send: {act.Answer.ToJsonString()}");
                    return StepOutcome.Planned;
                }

                var reply = await hub.SendActionAsync(act.Answer);
                var saved = archive.SaveTimestamped($"action-{act.Answer["action"]!.GetValue<string>()}", reply);
                say($"reply: HTTP {reply.Status} code {reply.Code?.ToString() ?? "?"} \"{reply.Message}\"{Highlights(reply)}  ({Path.GetFileName(saved)})");

                if (!reply.IsOk)
                    return StepOutcome.Failed;

                ledger.Record(act.Answer, reply.TryParseJson()!, state);
                return StepOutcome.Sent;

            default:
                throw new InvalidOperationException($"Unknown decision {decision}");
        }
    }

    /// <summary>Steps until the human is found, something stops the run, or the action cap is reached.</summary>
    public async Task<StepOutcome> RunAsync(int maxActions)
    {
        var outcome = StepOutcome.Stuck;
        for (var step = 1; step <= maxActions; step++)
        {
            say($"== step {step}/{maxActions} ==");
            outcome = await StepAsync(send: true);
            say(string.Empty);

            if (outcome != StepOutcome.Sent)
                return outcome;
        }

        say($"Action cap of {maxActions} reached; run again to continue from the live board.");
        return outcome;
    }

    private void Describe(OperationState state)
    {
        say($"state: {state}");

        var open = clusters
            .Select(cluster => (cluster.Name, Left: cluster.Fields.Count(field => !state.Inspected.Contains(field))))
            .Select(pair => $"{pair.Name} {pair.Left} left");
        say($"targets: {string.Join(", ", open)}; inspected {state.Inspected.Count}: {string.Join(" ", state.Inspected.Order())}");

        foreach (var entry in state.FreshLogs)
            say($"log [{entry.Field?.ToString() ?? "?"}] {entry.Message}");

        var overlay = new Dictionary<Coordinate, string>();
        foreach (var field in state.Inspected)
            overlay[field] = "--";
        foreach (var unit in state.Units)
            overlay[unit.Position] = unit.Type == UnitType.Transporter ? "TR" : "SC";
        say(map.Render(overlay));
    }

    private static string Highlights(HubReply reply)
    {
        var json = reply.TryParseJson();
        if (json is null)
            return string.Empty;

        var parts = new List<string>();
        foreach (var field in new[] { "spawn", "where", "path_steps", "action_points_left", "entries" })
            if (json[field] is { } value)
                parts.Add($"{field}={value}");
        if (json["spawned"] is JsonArray spawned)
            parts.Add($"spawned={string.Join(",", spawned.OfType<JsonObject>().Select(s => s["where"]?.GetValue<string>()))}");
        if (json["crew"] is JsonArray crew)
            parts.Add($"crew={crew.Count}");

        return parts.Count == 0 ? string.Empty : $" [{string.Join(" ", parts)}]";
    }
}
