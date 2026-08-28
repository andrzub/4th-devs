using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using _01_05_zadanie.Railway;

namespace _01_05_zadanie.Tools;

/// <summary>
/// The agent's only channel to the railway API. Named parameters are merged in next to "action",
/// matching the shape of the one documented example call, and the raw response is handed back
/// untouched so the model reads the API's own wording rather than a paraphrase of it.
/// </summary>
public sealed class RailwayApiTool : ITool
{
    private static readonly Regex FlagPattern = new(@"\{\{?FLG:[^}]+\}\}?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly RailwayClient _client;

    public RailwayApiTool(RailwayClient client) => _client = client;

    /// <summary>Set once a response carried a flag, so the loop can stop.</summary>
    public bool FlagFound { get; private set; }

    /// <summary>The flag exactly as the API returned it.</summary>
    public string? Flag { get; private set; }

    public string Name => "call_railway_api";

    public string Description =>
        "Call the railway control API. Pass the action name and, when the action takes arguments, a params " +
        "object whose keys are merged next to the action. Start with action \"help\" — the API documents itself. " +
        "Transport is handled for you: 503 responses and rate-limit windows are retried and waited out " +
        "automatically, so never retry a call yourself just because it failed in transit.";

    public JsonElement ParametersSchema { get; } = JsonSerializer.Deserialize<JsonElement>("""
        {
          "type": "object",
          "properties": {
            "action": {
              "type": "string",
              "description": "The action name, exactly as the API documentation spells it."
            },
            "params": {
              "type": "object",
              "description": "Arguments for the action, exactly as the API documentation names them. Omit when the action takes none.",
              "additionalProperties": true
            }
          },
          "required": ["action"],
          "additionalProperties": false
        }
        """);

    public async Task<string> ExecuteAsync(string argumentsJson, CancellationToken cancellationToken = default)
    {
        JsonObject answer;

        try
        {
            var parsed = JsonNode.Parse(argumentsJson)?.AsObject()
                         ?? throw new InvalidOperationException("arguments are not a JSON object");

            var action = parsed["action"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(action))
                return "The 'action' argument is required and must be a non-empty string.";

            answer = new JsonObject { ["action"] = action };

            if (parsed["params"] is JsonObject extra)
            {
                foreach (var (key, value) in extra)
                {
                    if (key == "action")
                        continue;

                    answer[key] = value?.DeepClone();
                }
            }
        }
        catch (Exception ex)
        {
            return $"Could not read the arguments: {ex.Message}";
        }

        RailwayCallResult result;
        try
        {
            result = await _client.CallAsync(answer, cancellationToken);
        }
        catch (Exception ex)
        {
            return $"The call could not be completed even after retrying: {ex.Message}. Do not retry immediately — reconsider the action first.";
        }

        var match = FlagPattern.Match(result.Body);
        if (match.Success)
        {
            FlagFound = true;
            Flag = match.Value;

            Console.WriteLine();
            Console.WriteLine($"=== FLAGA: {match.Value} ===");
            Console.WriteLine();

            return $"HTTP {result.StatusCode}. The response contains a flag, so the route is active and the task is done:\n{result.Body}\n" +
                   "Stop calling the API and finish with a short summary of the action sequence that worked.";
        }

        return $"HTTP {result.StatusCode} (rate limit: {result.RateLimit.Describe()}).\nResponse body:\n{result.Body}";
    }
}
