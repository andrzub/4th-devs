using _02_04_zadanie.Llm;
using _02_04_zadanie.Mailbox;
using _02_04_zadanie.Mission;
using _02_04_zadanie.Tools;

namespace _02_04_zadanie.Agents;

public sealed record AgentRunResult(
    string? FinalText,
    int Iterations,
    int PromptTokens,
    int CompletionTokens,
    bool FinishedByGoal,
    string? Abort);

/// <summary>
/// One function-calling loop, used by both roles in this run: the coordinator and every
/// researcher it delegates to. The only differences between an orchestrator and a worker are
/// the system prompt, the tool set and the goal, so they share this code.
/// </summary>
/// <param name="label">Prefix for console and transcript lines, e.g. "coordinator" or "researcher#2(date)".</param>
/// <param name="isGoalReached">
/// Checked by code, not by the model: the loop refuses to end on the model simply saying it is done.
/// </param>
public sealed class AgentLoop(
    string label,
    OpenAiCompatibleLlmClient client,
    string systemPrompt,
    IReadOnlyList<ITool> tools,
    Func<bool> isGoalReached,
    Transcript transcript,
    int maxIterations,
    int maxNudges = 3)
{
    private sealed record ToolOutcome(ToolCall Call, string Result, string? Abort);

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

                var outcomes = await ExecuteToolCallsAsync(response.ToolCalls, cancellationToken);
                foreach (var outcome in outcomes)
                    messages.Add(Message.ToolResult(outcome.Call.Id, outcome.Result));

                abort = outcomes.FirstOrDefault(o => o.Abort is not null)?.Abort;
                if (abort is not null)
                    break;

                continue;
            }

            finalText = response.Content;
            Console.WriteLine($"[{label}] {Preview(finalText ?? "", 400)}");
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
    /// Runs the calls of one turn. Every call gets a result, including the ones skipped after the
    /// goal was reached: the API rejects the next request if any tool call is left unanswered.
    /// </summary>
    private async Task<List<ToolOutcome>> ExecuteToolCallsAsync(IReadOnlyList<ToolCall> toolCalls, CancellationToken cancellationToken)
    {
        foreach (var call in toolCalls)
        {
            Console.WriteLine($"[{label}]   -> {call.FunctionName} {Preview(call.ArgumentsJson, 200)}");
            transcript.Append($"{label}: tool call {call.FunctionName}", call.ArgumentsJson);
        }

        var canRunInParallel = toolCalls.Count > 1 && toolCalls.All(call => ResolveTool(call)?.IsParallelSafe ?? true);
        List<ToolOutcome> outcomes;

        if (canRunInParallel)
        {
            Console.WriteLine($"[{label}]   ({toolCalls.Count} tool calls running in parallel)");
            outcomes = [.. await Task.WhenAll(toolCalls.Select(call => ExecuteAsync(call, cancellationToken)))];
        }
        else
        {
            outcomes = [];
            foreach (var call in toolCalls)
            {
                if (outcomes.Any(o => o.Abort is not null) || isGoalReached())
                {
                    outcomes.Add(new ToolOutcome(call, "Skipped: the mission was already finished by an earlier call in this turn.", null));
                    continue;
                }

                outcomes.Add(await ExecuteAsync(call, cancellationToken));
            }
        }

        foreach (var outcome in outcomes)
        {
            Console.WriteLine($"[{label}]   <- {Preview(outcome.Result, 300)}");
            transcript.Append($"{label}: tool result {outcome.Call.FunctionName}", outcome.Result);
        }

        return outcomes;
    }

    private async Task<ToolOutcome> ExecuteAsync(ToolCall call, CancellationToken cancellationToken)
    {
        var tool = ResolveTool(call);
        if (tool is null)
            return new ToolOutcome(call, $"Unknown tool '{call.FunctionName}'. Available: {string.Join(", ", tools.Select(t => t.Name))}.", null);

        try
        {
            return new ToolOutcome(call, await tool.ExecuteAsync(call.ArgumentsJson, cancellationToken), null);
        }
        catch (ZmailBudgetExceededException ex)
        {
            // Nothing the model can do about this one, so the run ends instead of retrying.
            return new ToolOutcome(call, ex.Message, ex.Message);
        }
        catch (Exception ex)
        {
            return new ToolOutcome(call, $"Tool '{call.FunctionName}' failed: {ex.Message}", null);
        }
    }

    private ITool? ResolveTool(ToolCall call) => tools.FirstOrDefault(t => t.Name == call.FunctionName);

    private static string Preview(string text, int maxLength)
    {
        var singleLine = text.ReplaceLineEndings(" ").Replace("\\n", " | ");
        return singleLine.Length <= maxLength ? singleLine : singleLine[..maxLength] + "...";
    }
}
