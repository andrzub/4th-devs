using System.Text.Json;
using _02_03_zadanie.Mission;

namespace _02_03_zadanie.Tools;

/// <summary>
/// Free local pre-flight of a digest: token count against the budget and validation against
/// the source log. Catches oversize, hallucinated and over-paraphrased lines before they cost
/// a submission. A digest that passes is remembered so submit_logs can send it by reference.
/// </summary>
public sealed class CheckDigestTool(DigestChecker checker, MissionState state) : ITool
{
    public string Name => "check_digest";

    public string Description =>
        "Checks a digest locally, for free: counts its tokens against the limit and validates every line against the " +
        "source log (format, date, HH:MM time, level and component must match a real entry; concrete markers of that " +
        "entry must be kept). Always run it before submit_logs and fix every problem. A digest that passes is " +
        "remembered, so submit_logs can then be called without arguments to send exactly that text.";

    public JsonElement ParametersSchema { get; } = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "logs": {
              "type": "string",
              "description": "The digest: one event per line, lines separated by newline characters."
            }
          },
          "required": ["logs"]
        }
        """).RootElement;

    public Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        var logs = ToolArguments.Parse(argumentsJson).GetString("logs");
        if (string.IsNullOrWhiteSpace(logs))
            return Task.FromResult("Missing 'logs'.");

        var result = checker.Check(logs);
        if (!result.Passed)
            return Task.FromResult(result.Render());

        state.RememberCheckedDigest(result.Digest);
        return Task.FromResult(result.Render() + "This digest is remembered: call submit_logs without arguments to send it.");
    }
}
