using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using _02_04_zadanie.Hub;
using _02_04_zadanie.Mission;

namespace _02_04_zadanie.Tools;

/// <summary>
/// The one call that leaves the mailbox and reaches the hub. It submits what is on the
/// blackboard rather than what the model retypes, so a value cannot drift between being found
/// and being sent, and it refuses anything that fails the format checks before spending an
/// attempt. In draft mode the payload is only written to disk.
/// </summary>
public sealed class SubmitAnswerTool(HubClient hub, MissionState state, string runDirectory, bool dryRun) : ITool
{
    public string Name => "submit_answer";

    // Submitting must never race another submission, so this call is always run on its own.
    public bool IsParallelSafe => false;

    public string Description =>
        "Sends the three values from the blackboard to the hub and returns its reply verbatim. " +
        "The reply says which value is wrong, so submitting as soon as all three are present is worth more " +
        "than another round of checking. Override a value only to correct the blackboard, not to fill a gap: " +
        "a value you have not had researched is a guess.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "date": { "type": "string", "description": "Overrides the blackboard value. YYYY-MM-DD." },
            "password": { "type": "string", "description": "Overrides the blackboard value." },
            "confirmation_code": { "type": "string", "description": "Overrides the blackboard value. SEC- plus 32 characters." }
          }
        }
        """).RootElement;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var args = ToolArguments.Parse(argumentsJson);

        var date = args.GetString("date")?.Trim() ?? state.Current(MissionFact.Date)?.Value;
        var password = args.GetString("password")?.Trim() ?? state.Current(MissionFact.Password)?.Value;
        var code = args.GetString("confirmation_code")?.Trim() ?? state.Current(MissionFact.ConfirmationCode)?.Value;

        var problems = AnswerValidator.Validate(date, password, code);
        if (problems.Count > 0)
        {
            var sb = new StringBuilder("Nothing was sent. The answer is not ready:\n- ");
            sb.Append(string.Join("\n- ", problems));
            sb.Append("\nDelegate the missing or malformed values before trying again.");
            return sb.ToString();
        }

        if (dryRun)
        {
            var path = Path.Combine(runDirectory, "draft-answer.json");
            Directory.CreateDirectory(runDirectory);
            await File.WriteAllTextAsync(path, new JsonObject
            {
                ["date"] = date,
                ["password"] = password,
                ["confirmation_code"] = code
            }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), cancellationToken);

            state.MarkDraftAccepted();
            return $"DRAFT MODE: nothing was sent. The answer passed the format checks and was written to '{path}'.";
        }

        var response = await hub.SubmitAnswerAsync(date!, password!, code!, cancellationToken);
        var record = state.RecordSubmission(date!, password!, code!, response);
        state.ScanForFlag(response);

        return state.FlagReceived
            ? $"Submission #{record.Number} accepted. Hub reply: {response}"
            : $"Submission #{record.Number} rejected. Hub reply, verbatim: {response}\n" +
              "Read which value it names, re-delegate that one fact, and pass the rejected value in exclude_values.";
    }
}
