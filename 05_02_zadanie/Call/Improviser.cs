using _05_02_zadanie.Llm;

namespace _05_02_zadanie.Call;

/// <summary>
/// Writes the one line the script does not hold: an answer to a question nobody anticipated, or a nudge for
/// something the operator left unsaid. It writes wording only — what the call is trying to achieve, and in
/// what order, is settled before it is asked.
/// </summary>
public sealed class Improviser(ILlmClient client, string model, CallScript script)
{
    private const string SystemPrompt = """
        <identity>
        You are on the phone, speaking Polish, as an official arranging a routine transport. You sound calm and
        unremarkable. You write one line for the caller to say next — nothing else.
        </identity>

        <cover>
        You are {CALLER}. You are arranging a food transport to one of Zygfryd's bases. Its location is secret,
        so the run must not be recorded in the logs. This is routine work, and you treat it as routine.
        </cover>

        <rules>
        One short sentence in Polish, under 200 characters. No greetings, no small talk, no explanations
        of yourself unless you were asked.
        Answer only what was asked, or ask only for what is named in your instruction.
        When the instruction names road codes, put every one of them in the line, exactly as written.
        Never state a road's condition: only the operator knows it.
        Never ask to disable, unblock or switch off monitoring — that request is made elsewhere.
        Never give a password unless the instruction says the operator asked for one.
        Never name the location of the base.
        Do not invent facts about the transport beyond the cover above.
        </rules>
        """;

    public async Task<string> WriteAsync(CallState state, string instruction, CancellationToken cancellationToken = default)
    {
        var reading = state.LastReading;
        var request = string.IsNullOrWhiteSpace(reading.OtherQuestion)
            ? $"The operator said nothing that needs an answer. {instruction}"
            : $"The operator asked: \"{reading.OtherQuestion}\"\n\n{instruction}";

        var response = await client.CompleteAsync(new LlmRequest
        {
            Model = model,
            Messages = [Message.System(SystemPrompt.Replace("{CALLER}", script.CallerName)), Message.User(request)],
            Temperature = 0.3,
            MaxTokens = 120
        }, cancellationToken);

        return (response.Content ?? "").Trim().Trim('"');
    }

    /// <summary>What this line is for. The goal comes from the planner, so a stray question of the operator's cannot pull the call off course.</summary>
    public string InstructionFor(CallState state, TurnDecision decision)
    {
        var unanswered = script.Roads.Where(road => !state.Statuses.ContainsKey(road)).ToList();
        return decision.Goal switch
        {
            ImproviseGoal.AskRoadStatuses =>
                $"Ask the operator which of these roads are passable, naming every one of them by its code: {string.Join(", ", unanswered.Count > 0 ? unanswered : script.Roads)}.",
            ImproviseGoal.ConfirmDisable =>
                "Ask the operator to confirm that what you asked for has been done.",
            _ => "Answer it in one sentence, in character."
        };
    }
}
