namespace _01_02_zadanie.LLM;

public enum MessageRole
{
    System,
    User,
    Assistant,
    Tool
}

public class Message
{
    public MessageRole Role { get; }
    public string? Content { get; }

    /// <summary>
    /// Only set when Role == Tool. Corresponds to the tool_call_id that triggered this result.
    /// </summary>
    public string? ToolCallId { get; }

    /// <summary>
    /// Only set when Role == Assistant and the model requested tool calls.
    /// Must be echoed back to the API so the following Tool messages are accepted.
    /// </summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; }

    private Message(MessageRole role, string? content, string? toolCallId = null, IReadOnlyList<ToolCall>? toolCalls = null)
    {
        Role = role;
        Content = content;
        ToolCallId = toolCallId;
        ToolCalls = toolCalls;
    }

    public static Message System(string content) => new(MessageRole.System, content);
    public static Message User(string content) => new(MessageRole.User, content);
    public static Message Assistant(string content) => new(MessageRole.Assistant, content);
    public static Message AssistantToolCalls(string? content, IReadOnlyList<ToolCall> toolCalls) => new(MessageRole.Assistant, content, toolCalls: toolCalls);
    public static Message ToolResult(string toolCallId, string content) => new(MessageRole.Tool, content, toolCallId);

    public string RoleName => Role switch
    {
        MessageRole.System    => "system",
        MessageRole.User      => "user",
        MessageRole.Assistant => "assistant",
        MessageRole.Tool      => "tool",
        _                     => throw new ArgumentOutOfRangeException()
    };
}
