namespace _03_01_zadanie.Llm;

public enum MessageRole
{
    System,
    User,
    Assistant
}

public class Message
{
    public MessageRole Role { get; }
    public string? Content { get; }

    private Message(MessageRole role, string? content)
    {
        Role = role;
        Content = content;
    }

    public static Message System(string content) => new(MessageRole.System, content);
    public static Message User(string content) => new(MessageRole.User, content);
    public static Message Assistant(string content) => new(MessageRole.Assistant, content);

    public string RoleName => Role switch
    {
        MessageRole.System    => "system",
        MessageRole.User      => "user",
        MessageRole.Assistant => "assistant",
        _                     => throw new ArgumentOutOfRangeException()
    };
}
