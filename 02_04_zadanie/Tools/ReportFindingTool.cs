using System.Text.Json;
using System.Text.RegularExpressions;
using _02_04_zadanie.Agents;
using _02_04_zadanie.Mission;

namespace _02_04_zadanie.Tools;

/// <summary>
/// The researcher's only way to finish. It is also where the guarantees live: a value in the
/// wrong format, without a quote, without the message it came from, or already known to be
/// wrong is refused here, and the researcher keeps working. Enforcing that in code rather than
/// in the prompt is what stops a plausible-looking guess from reaching the coordinator.
/// </summary>
public sealed partial class ReportFindingTool(ResearchAssignment assignment, MissionState state, string researcherLabel) : ITool
{
    [GeneratedRegex("^[0-9a-fA-F]{32}$")]
    private static partial Regex MessageIdRegex();

    public ResearchReport? Report { get; private set; }

    public string Name => "report_finding";

    public string Description =>
        "Hands your answer back to the coordinator and ends your work. Call it exactly once. " +
        "Report found=true only for a value you have read in a message body, and quote that body verbatim in " +
        "'evidence'. If you could not find it, report found=false and describe in 'notes' which queries you ran " +
        "and what you saw, so the coordinator can decide what to try next.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "found": {
              "type": "boolean",
              "description": "True only if you read the value in a message body."
            },
            "value": {
              "type": "string",
              "description": "The value itself and nothing else: no label, no quotes, no surrounding sentence."
            },
            "evidence": {
              "type": "string",
              "description": "The sentence from the message body that contains the value, copied verbatim."
            },
            "message_id": {
              "type": "string",
              "description": "The 32-character messageID of the message the value came from."
            },
            "notes": {
              "type": "string",
              "description": "Anything the coordinator should know: competing candidates, queries you ran, doubts."
            }
          },
          "required": ["found"]
        }
        """).RootElement;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = ToolArguments.Parse(argumentsJson);
        var found = args.GetBool("found");
        if (found is null)
            return Task.FromResult("Missing 'found'. Pass true if you read the value in a message, false if you could not find it.");

        var value = args.GetString("value")?.Trim();
        var evidence = args.GetString("evidence")?.Trim();
        var messageId = args.GetString("message_id")?.Trim();
        var notes = args.GetString("notes")?.Trim();

        if (found == false)
        {
            if (string.IsNullOrWhiteSpace(notes))
                return Task.FromResult("A negative report needs 'notes': list the queries you ran and what you saw, so the effort is not repeated blindly.");

            Report = new ResearchReport(false, null, null, null, notes);
            return Task.FromResult("Recorded as not found. Your work here is done.");
        }

        if (assignment.Fact is not { } fact)
        {
            if (string.IsNullOrWhiteSpace(notes))
                return Task.FromResult("This is a reconnaissance assignment, so put your observations in 'notes'.");

            Report = new ResearchReport(true, value, evidence, messageId, notes);
            return Task.FromResult("Observations recorded. Your work here is done.");
        }

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(value))
            problems.Add("'value' is empty.");
        if (string.IsNullOrWhiteSpace(evidence))
            problems.Add("'evidence' is empty: quote the sentence from the message body that contains the value.");
        if (string.IsNullOrWhiteSpace(messageId))
            problems.Add("'message_id' is empty: give the 32-character messageID of the message you read it in.");
        else if (!MessageIdRegex().IsMatch(messageId))
            problems.Add($"'{messageId}' is not a messageID. Use the 32-character hash from the listing, not a rowID: rowID values shift as new mail arrives.");

        if (value is not null)
        {
            problems.AddRange(AnswerValidator.ValidateFact(fact, value));

            if (assignment.ExcludeValues.Contains(value, StringComparer.Ordinal) || state.IsRejected(fact, value))
                problems.Add($"\"{value}\" is already known to be wrong. Keep looking for a different one.");
        }

        if (problems.Count > 0)
            return Task.FromResult("Report refused, so it has not been handed over:\n- " + string.Join("\n- ", problems));

        var report = new ResearchReport(true, value, evidence, messageId, notes);
        report.Outcome = state.RecordFinding(new Finding(fact, value!, evidence!, messageId!, researcherLabel, notes));
        Report = report;

        return Task.FromResult($"Recorded {FactNames.ToApiName(fact)} = {value} ({report.Outcome}). Your work here is done.");
    }
}
