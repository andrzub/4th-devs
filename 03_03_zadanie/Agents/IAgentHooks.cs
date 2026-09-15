using _03_03_zadanie.Llm;

namespace _03_03_zadanie.Agents;

/// <summary>
/// The points at which the run can step into the agent's loop. They are not background bookkeeping:
/// a refusal returned before a tool call keeps the robot alive, and a demand returned before the
/// agent finishes keeps it working until the module is actually delivered.
/// </summary>
public interface IAgentHooks
{
    /// <summary>Returning a string cancels the call and hands that text back as its result.</summary>
    Task<string?> BeforeToolCallAsync(ToolCall call, CancellationToken cancellationToken = default);

    /// <summary>Returns the result the model will read, with whatever context the run wants to add.</summary>
    Task<string> AfterToolResultAsync(ToolCall call, string result, CancellationToken cancellationToken = default);

    /// <summary>Returning a string refuses the agent's attempt to stop and sends it back to work.</summary>
    Task<string?> BeforeFinishAsync(CancellationToken cancellationToken = default);
}
