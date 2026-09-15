using _03_03_zadanie.Llm;
using _03_03_zadanie.Mission;
using _03_03_zadanie.Reactor;
using _03_03_zadanie.Tools;

namespace _03_03_zadanie.Agents;

public sealed record AgentRunResult(string? FinalText, int Iterations, int PromptTokens, int CompletionTokens, bool FinishedByGoal, string? Abort);

/// <summary>
/// The function-calling loop, with hooks around every tool call and around the agent's attempt to
/// stop. Calls run one after another rather than concurrently: each one ticks a stateful reactor,
/// and the guard's picture of it only holds while commands arrive in a known order.
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
    IAgentHooks? hooks = null,
    int maxNudges = 3)
{
    public async Task<AgentRunResult> RunAsync(string task, CancellationToken cancellationToken = default)
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

            var response = await client.CompleteAsync(new LlmRequest { Messages = messages, Tools = [.. tools], Temperature = 0 }, cancellationToken);
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
            Console.WriteLine($"[{label}] {Preview(finalText ?? string.Empty, 500)}");
            transcript.Append($"{label}: assistant", finalText ?? string.Empty);
            messages.Add(Message.Assistant(finalText ?? string.Empty));

            if (isGoalReached())
                break;

            var demand = hooks is null ? null : await hooks.BeforeFinishAsync(cancellationToken);
            if (demand is null)
                break;

            // Stopping short of the goal is never a valid end state, so the agent is sent back to
            // work a bounded number of times.
            if (++nudges > maxNudges)
            {
                abort = $"{label} stopped working after {nudges - 1} reminders.";
                break;
            }

            messages.Add(Message.User(demand));
            transcript.Append($"{label}: demand", demand);
        }

        if (abort is null && iteration >= maxIterations && !isGoalReached())
            abort = $"{label} hit the {maxIterations}-iteration limit without reaching the goal.";

        return new AgentRunResult(finalText, iteration, promptTokens, completionTokens, isGoalReached(), abort);
    }

    /// <summary>
    /// Runs one call. Calls left over after the run is already settled still get a result: the API
    /// rejects the next request while any tool call in the turn is unanswered.
    /// </summary>
    private async Task<(string Result, string? Abort)> ExecuteAsync(ToolCall call, bool alreadySettled, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{label}]   -> {call.FunctionName} {Preview(call.ArgumentsJson, 300)}");
        transcript.Append($"{label}: tool call {call.FunctionName}", call.ArgumentsJson);

        var (result, abort) = await InvokeAsync(call, alreadySettled, cancellationToken);

        if (hooks is not null)
            result = await hooks.AfterToolResultAsync(call, result, cancellationToken);

        Console.WriteLine($"[{label}]   <- {Preview(result, 400)}");
        transcript.Append($"{label}: tool result {call.FunctionName}", result);
        return (result, abort);
    }

    private async Task<(string Result, string? Abort)> InvokeAsync(ToolCall call, bool alreadySettled, CancellationToken cancellationToken)
    {
        if (alreadySettled)
            return ("Skipped: the run was already settled by an earlier call in this turn.", null);

        var tool = tools.FirstOrDefault(candidate => candidate.Name == call.FunctionName);
        if (tool is null)
            return ($"Unknown tool '{call.FunctionName}'. Available: {string.Join(", ", tools.Select(candidate => candidate.Name))}.", null);

        if (hooks is not null && await hooks.BeforeToolCallAsync(call, cancellationToken) is { } refusal)
            return (refusal, null);

        try
        {
            return (await tool.ExecuteAsync(call.ArgumentsJson, cancellationToken), null);
        }
        catch (ReactorBudgetExceededException ex)
        {
            // Nothing the model can do about this, so the run ends instead of retrying.
            return (ex.Message, ex.Message);
        }
        catch (Exception ex)
        {
            return ($"Tool '{call.FunctionName}' failed: {ex.Message}", null);
        }
    }

    private static string Preview(string text, int maxLength)
    {
        var singleLine = text.ReplaceLineEndings(" ").Replace("\n", " | ");
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "...";
    }
}
