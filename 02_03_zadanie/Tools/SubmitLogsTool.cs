using System.Text.Json;
using _02_03_zadanie.Hub;
using _02_03_zadanie.Mission;

namespace _02_03_zadanie.Tools;

/// <summary>
/// Sends a digest to the hub. Runs the same checks as check_digest first and refuses to send a
/// digest that fails them, so no submission is wasted on a formality. Called without arguments
/// it sends the last digest that passed check_digest, which spares the model from repeating
/// the whole text. Every digest sent is saved to the run directory first. In dry-run mode
/// nothing is sent: the first digest that passes the checks ends the run.
/// </summary>
public sealed class SubmitLogsTool(HubClient hub, DigestChecker checker, MissionState state, string runDirectory, bool dryRun) : ITool
{
    public string Name => "submit_logs";

    public string Description =>
        "Sends a digest to the technicians for verification and returns their feedback verbatim. Called without " +
        "arguments it sends the last digest that passed check_digest. Pass 'logs' only to send a different text; it " +
        "is checked the same way and refused if it has format problems or exceeds the safe token limit. Every call " +
        "that passes the checks is a real submission.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "logs": {
              "type": "string",
              "description": "Optional. A digest to send instead of the last one that passed check_digest."
            }
          }
        }
        """).RootElement;

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var logs = ToolArguments.Parse(argumentsJson).GetString("logs");
        if (string.IsNullOrWhiteSpace(logs))
            logs = state.LastCheckedDigest;
        if (string.IsNullOrWhiteSpace(logs))
            return "Nothing to submit: run check_digest on a digest first, or pass 'logs'.";

        var result = checker.Check(logs);
        if (!result.Passed)
            return "NOT SUBMITTED. Fix these first:\n" + result.Render();

        var number = state.NextSubmissionNumber();
        var savedPath = Path.Combine(runDirectory, $"digest-{number:00}.txt");
        Directory.CreateDirectory(runDirectory);
        await File.WriteAllTextAsync(savedPath, result.Digest, cancellationToken);

        if (dryRun)
        {
            state.MarkDraftAccepted();
            Console.WriteLine($"  [submit_logs] DRY RUN: digest #{number} ({result.LineCount} lines, {result.Budget.Tokens} tokens) saved to {savedPath}, nothing sent.");
            return $"DRY RUN: nothing was sent to the hub. The digest passed the checks ({result.LineCount} lines, {result.Budget.Tokens} tokens) " +
                   $"and was saved to {savedPath}. The run ends here; report that the draft is ready.";
        }

        Console.WriteLine($"  [submit_logs] submission #{number}: {result.LineCount} lines, {result.Budget.Tokens} tokens...");
        var response = await hub.SubmitLogsAsync(result.Digest, result.Budget.Tokens, cancellationToken);
        Console.WriteLine($"  [submit_logs] {response}");

        state.ScanForFlag(response);
        return response;
    }
}
