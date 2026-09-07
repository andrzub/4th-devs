using System.Text;
using System.Text.Json;
using _02_04_zadanie.Agents;
using _02_04_zadanie.Mission;

namespace _02_04_zadanie.Tools;

/// <summary>
/// The coordinator's one way to get anything done: it opens a thread for a fresh researcher
/// with its own system prompt, its own tools and its own context, and the researcher's answer
/// becomes this call's result. Several of these in one turn run in parallel.
/// </summary>
public sealed class DelegateTool(ResearcherRunner runner, MissionState state) : ITool
{
    public string Name => "delegate";

    public string Description =>
        "Hands one fact to a fresh researcher with access to the mailbox, and returns what it found. " +
        "The researcher starts with an empty context and sees only your briefing, so write the briefing as if " +
        "to someone who has never heard of this mission. Issue several delegate calls in one turn to run " +
        "researchers at the same time.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "fact": {
              "type": "string",
              "enum": ["date", "password", "confirmation_code", "recon"],
              "description": "Which value the researcher must bring back. 'recon' means no single value: it just reports what is in the mailbox."
            },
            "briefing": {
              "type": "string",
              "description": "Self-contained instructions: what the value is, how Polish mail would word it, who likely sent it, which candidates would be wrong, and what has already been tried."
            },
            "exclude_values": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Values already known to be wrong. The researcher is blocked in code from reporting any of them."
            }
          },
          "required": ["fact", "briefing"]
        }
        """).RootElement;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = ToolArguments.Parse(argumentsJson);

        var factName = args.GetString("fact")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(factName))
            return "Missing 'fact'. Use date, password, confirmation_code or recon.";

        MissionFact? fact = null;
        if (factName != "recon")
        {
            fact = FactNames.TryParse(factName);
            if (fact is null)
                return $"Unknown fact '{factName}'. Use date, password, confirmation_code or recon.";
        }

        var briefing = args.GetString("briefing")?.Trim();
        if (string.IsNullOrWhiteSpace(briefing))
            return "Missing 'briefing'. The researcher sees nothing but this text, so it cannot be empty.";

        var excluded = (args.GetStringArray("exclude_values") ?? [])
            .Select(v => v.Trim())
            .Where(v => v.Length > 0)
            .ToList();

        // Rejections are remembered on the blackboard, so they also bind researchers delegated later.
        if (fact is { } slot)
            foreach (var value in excluded)
                state.RejectValue(slot, value);

        var result = await runner.RunAsync(new ResearchAssignment(fact, briefing, excluded), cancellationToken);
        return Format(result);
    }

    private static string Format(ResearcherResult result)
    {
        var sb = new StringBuilder();
        var report = result.Report;

        if (report is null)
        {
            sb.AppendLine($"{result.Label} came back with nothing usable: {result.Run.Abort ?? "it never filed a report."}");
            sb.AppendLine("Nothing was written to the blackboard. Decide whether to delegate this again with a sharper briefing.");
        }
        else if (!report.Found)
        {
            sb.AppendLine($"{result.Label} reported NOT FOUND.");
            sb.AppendLine($"What it tried and saw: {report.Notes}");
            sb.AppendLine("The mailbox is live, so this may be worth another attempt later, or from a different angle.");
        }
        else
        {
            sb.AppendLine($"{result.Label} reported FOUND.");
            if (report.Value is not null)
                sb.AppendLine($"value: {report.Value}");
            if (report.Evidence is not null)
                sb.AppendLine($"evidence, quoted from the message body: \"{report.Evidence}\"");
            if (report.MessageId is not null)
                sb.AppendLine($"from message: {report.MessageId}");
            if (report.Notes is { Length: > 0 })
                sb.AppendLine($"notes: {report.Notes}");

            sb.AppendLine(report.Outcome switch
            {
                FindingOutcome.Accepted => "blackboard: recorded as the current value for this fact.",
                FindingOutcome.Confirmed => "blackboard: matches what another researcher reported independently.",
                FindingOutcome.Conflict => "blackboard: CONFLICT. Another researcher reported a different value for this fact. " +
                                           "Check mission_status and settle it before submitting.",
                FindingOutcome.PreviouslyRejected => "blackboard: this value is already known to be wrong, so it was not made current.",
                _ => "blackboard: nothing to record for a reconnaissance assignment."
            });
        }

        sb.Append($"({result.Label} ran {result.Run.Iterations} iterations and read {result.Run.PromptTokens} prompt tokens, none of which entered your context.)");
        return sb.ToString();
    }
}
