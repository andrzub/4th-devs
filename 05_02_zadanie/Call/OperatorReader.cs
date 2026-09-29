using System.Text.Json;
using _05_02_zadanie.Llm;
using _05_02_zadanie.Voice;

namespace _05_02_zadanie.Call;

/// <summary>
/// Turns the operator's words into <see cref="OperatorReading"/>. The model reads; it never decides what to
/// say back. A road status it reports is dropped unless the operator's own words name that road, because a
/// status invented here becomes a request to unblock a road nobody cleared.
/// </summary>
public sealed class OperatorReader(ILlmClient client, string model, IReadOnlyList<string> roads)
{
    private const string SystemPrompt = """
        <identity>
        You transcribe meaning, not intent. You are reading one turn of a phone call from a road network
        operator to a caller. The operator speaks Polish. You never decide what the caller should say next.
        </identity>

        <task>
        Report only what the operator's words say:
        - roads: for every road code the operator mentions, whether they call it passable (przejezdna,
          czysta, nieskażona, otwarta, można jechać) or blocked (nieprzejezdna, skażona, zamknięta,
          zablokowana, nie polecam, uszkodzona). Use "unknown" when they name a road without saying which.
          Quote their own words for each status, verbatim.
        - asks_for_password: true when they ask for a password, a code, or any authorisation phrase.
        - asks_why: true only when they press for the reason behind something the caller has already asked
          them to do. An opening question — what the call is about, in what matter, how they can help — is not
          this, and neither is a question about who the caller is or what their order number is: those belong
          in other_question.
        - other_question: any other question or demand of theirs, in Polish, verbatim. Empty when there is none.
        - monitoring_disabled: road codes whose monitoring they say is now off. Only when it is done,
          not when they promise or consider it.
        - call_burned: true only when they say the call is over, refuse to continue, raise an alarm, or accuse
          the caller. Suspicion or a request for a password is not a burned call.
        - summary: one short English sentence on what they said.
        </task>

        <rules>
        Report nothing the words do not carry. An empty or unintelligible turn is all false and empty lists.
        The transcript comes from speech recognition, so road codes may arrive spaced or hyphenated
        (RD 224, RD-224); read them as the same code.
        </rules>
        """;

    private static readonly JsonElement Schema = JsonDocument.Parse("""
        {
          "name": "operator_reading",
          "strict": true,
          "schema": {
            "type": "object",
            "additionalProperties": false,
            "required": ["roads", "asks_for_password", "asks_why", "other_question", "monitoring_disabled", "call_burned", "summary"],
            "properties": {
              "roads": {
                "type": "array",
                "items": {
                  "type": "object",
                  "additionalProperties": false,
                  "required": ["code", "status", "quote"],
                  "properties": {
                    "code": { "type": "string" },
                    "status": { "type": "string", "enum": ["passable", "blocked", "unknown"] },
                    "quote": { "type": "string" }
                  }
                }
              },
              "asks_for_password": { "type": "boolean" },
              "asks_why": { "type": "boolean" },
              "other_question": { "type": "string" },
              "monitoring_disabled": { "type": "array", "items": { "type": "string" } },
              "call_burned": { "type": "boolean" },
              "summary": { "type": "string" }
            }
          }
        }
        """).RootElement;

    public async Task<OperatorReading> ReadAsync(string operatorWords, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(operatorWords))
            return OperatorReading.Silent;

        var response = await client.CompleteAsync(new LlmRequest
        {
            Model = model,
            Messages = [Message.System(SystemPrompt), Message.User($"The operator said:\n\n{operatorWords}")],
            ResponseFormat = Schema,
            Temperature = 0
        }, cancellationToken);

        return Parse(response.Content ?? "", operatorWords, roads);
    }

    internal static OperatorReading Parse(string json, string operatorWords, IReadOnlyList<string> roads)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var heard = TranscriptCheck.Normalize(operatorWords);

        var readings = new List<RoadReading>();
        foreach (var item in root.GetProperty("roads").EnumerateArray())
        {
            var code = MatchKnownRoad(item.GetProperty("code").GetString() ?? "", roads);
            if (code is null || !heard.Contains(TranscriptCheck.Normalize(code), StringComparison.Ordinal))
                continue;

            var status = item.GetProperty("status").GetString() switch
            {
                "passable" => RoadStatus.Passable,
                "blocked" => RoadStatus.Blocked,
                _ => RoadStatus.Unknown
            };
            readings.Add(new RoadReading(code, status, item.GetProperty("quote").GetString() ?? ""));
        }

        var disabled = root.GetProperty("monitoring_disabled").EnumerateArray()
            .Select(item => MatchKnownRoad(item.GetString() ?? "", roads))
            .OfType<string>()
            .Where(code => heard.Contains(TranscriptCheck.Normalize(code), StringComparison.Ordinal))
            .Distinct()
            .ToList();

        return new OperatorReading(
            readings.DistinctBy(reading => reading.Code).ToList(),
            root.GetProperty("asks_for_password").GetBoolean(),
            root.GetProperty("asks_why").GetBoolean(),
            root.GetProperty("other_question").GetString()?.Trim() ?? "",
            disabled,
            root.GetProperty("call_burned").GetBoolean(),
            root.GetProperty("summary").GetString()?.Trim() ?? "");
    }

    private static string? MatchKnownRoad(string code, IReadOnlyList<string> roads)
    {
        var normalized = TranscriptCheck.Normalize(code);
        return normalized.Length == 0 ? null : roads.FirstOrDefault(road => TranscriptCheck.Normalize(road) == normalized);
    }
}
