using _02_05_zadanie.Hub;
using _02_05_zadanie.Llm;
using _02_05_zadanie.Mission;
using _02_05_zadanie.Tools;

namespace _02_05_zadanie.Agents;

public sealed record AgentRunResult(
    string? FinalText,
    int Iterations,
    int PromptTokens,
    int CompletionTokens,
    bool FinishedByGoal,
    string? Abort);

/// <summary>
/// The function-calling loop. Tool calls run one after another rather than concurrently: the
/// single tool here changes the state of a real drone and spends a metered submission, so there
/// is nothing to gain from racing them and a great deal to lose.
/// </summary>
/// <param name="isGoalReached">
/// Checked by code, not by the model: the loop refuses to end on the model announcing success.
/// </param>
public sealed class AgentLoop(
    string label,
    ILlmClient client,
    string systemPrompt,
    IReadOnlyList<ITool> tools,
    Func<bool> isGoalReached,
    Transcript transcript,
    int maxIterations,
    int maxNudges = 3)
{
    public async Task<AgentRunResult> RunAsync(string task, Func<int, string> nudge, CancellationToken cancellationToken = default)
    {
        var messages = new List<Message> { Message.System(systemPrompt), Message.User(task) };
        transcript.Append($"{label}: system", systemPrompt);
        transcript.Append($"{label}: task", task);

        var promptTokens = 0;
        var completionTokens = 0;
        var nudges = 0;
        var iteration = 0;
        string? finalText = null;
        string? abort = null;

        while (iteration < maxIterations && !isGoalReached())
        {
            iteration++;
            Console.WriteLine($"[{label}] iteration {iteration}");

            var response = await client.CompleteAsync(
                new LlmRequest { Messages = messages, Tools = [.. tools], Temperature = 0 }, cancellationToken);
            promptTokens += response.Usage.PromptTokens;
            completionTokens += response.Usage.CompletionTokens;

            if (response.HasToolCalls)
            {
                messages.Add(Message.AssistantToolCalls(response.ContentRaw, response.ToolCalls));

                foreach (var call in response.ToolCalls)
                {
                    var (result, callAbort) = await ExecuteAsync(call, abort is not null || isGoalReached(), cancellationToken);
                    messages.Add(Message.ToolResult(call.Id, result));
                    abort ??= callAbort;
                }

                if (abort is not null)
                    break;

                continue;
            }

            finalText = response.Content;
            Console.WriteLine($"[{label}] {Preview(finalText ?? "", 500)}");
            transcript.Append($"{label}: assistant", finalText ?? "");
            messages.Add(Message.Assistant(finalText ?? ""));

            if (isGoalReached())
                break;

            // Text with no tool call and no result is never a valid end state, so the agent is
            // pushed back to work a bounded number of times.
            if (++nudges > maxNudges)
            {
                abort = $"{label} stopped producing tool calls after {nudges - 1} reminders.";
                break;
            }

            var reminder = nudge(nudges);
            messages.Add(Message.User(reminder));
            transcript.Append($"{label}: reminder", reminder);
        }

        if (abort is null && iteration >= maxIterations && !isGoalReached())
            abort = $"{label} hit the {maxIterations}-iteration limit without reaching its goal.";

        return new AgentRunResult(finalText, iteration, promptTokens, completionTokens, isGoalReached(), abort);
    }

    /// <summary>
    /// Runs one call. Calls left over after the run is already settled still get a result:
    /// the API rejects the next request while any tool call in the turn is unanswered.
    /// </summary>
    private async Task<(string Result, string? Abort)> ExecuteAsync(ToolCall call, bool alreadySettled, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{label}]   -> {call.FunctionName} {Preview(call.ArgumentsJson, 300)}");
        transcript.Append($"{label}: tool call {call.FunctionName}", call.ArgumentsJson);

        var (result, abort) = await InvokeAsync(call, alreadySettled, cancellationToken);

        Console.WriteLine($"[{label}]   <- {Preview(result, 400)}");
        transcript.Append($"{label}: tool result {call.FunctionName}", result);
        return (result, abort);
    }

    private async Task<(string Result, string? Abort)> InvokeAsync(ToolCall call, bool alreadySettled, CancellationToken cancellationToken)
    {
        if (alreadySettled)
            return ("Skipped: the run was already settled by an earlier call in this turn.", null);

        var tool = tools.FirstOrDefault(t => t.Name == call.FunctionName);
        if (tool is null)
            return ($"Unknown tool '{call.FunctionName}'. Available: {string.Join(", ", tools.Select(t => t.Name))}.", null);

        try
        {
            return (await tool.ExecuteAsync(call.ArgumentsJson, cancellationToken), null);
        }
        catch (HubBudgetExceededException ex)
        {
            // Nothing the model can do about this one, so the run ends instead of retrying.
            return (ex.Message, ex.Message);
        }
        catch (Exception ex)
        {
            return ($"Tool '{call.FunctionName}' failed: {ex.Message}", null);
        }
    }

    private static string Preview(string text, int maxLength)
    {
        var singleLine = text.ReplaceLineEndings(" ").Replace("\\n", " | ");
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "...";
    }
}
