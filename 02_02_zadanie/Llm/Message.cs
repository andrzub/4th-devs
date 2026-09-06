namespace _02_02_zadanie.Llm;

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

    /// <summary>
    /// Images attached to a user message, each as a complete <c>data:</c> URL.
    /// When present the message is serialised as a content-parts array instead of a plain string,
    /// which is the only shape the API accepts for vision input.
    /// </summary>
    public IReadOnlyList<string>? ImageDataUrls { get; }

    private Message(MessageRole role, string? content, string? toolCallId = null, IReadOnlyList<ToolCall>? toolCalls = null, IReadOnlyList<string>? imageDataUrls = null)
    {
        Role = role;
        Content = content;
        ToolCallId = toolCallId;
        ToolCalls = toolCalls;
        ImageDataUrls = imageDataUrls;
    }

    public static Message System(string content) => new(MessageRole.System, content);
    public static Message User(string content) => new(MessageRole.User, content);
    public static Message UserWithImages(string content, IReadOnlyList<string> imageDataUrls) => new(MessageRole.User, content, imageDataUrls: imageDataUrls);
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
