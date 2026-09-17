namespace _03_04_zadanie;

public sealed class TaskSettings
{
    public string VerifyUrl { get; set; } = "https://hub.ag3nts.org/verify";

    public string TaskName { get; set; } = "negotiations";

    public string AI_DevsApiKey { get; set; } = string.Empty;

    public string ResolveApiKey() =>
        string.IsNullOrWhiteSpace(AI_DevsApiKey)
            ? Environment.GetEnvironmentVariable("AI_DEVS_API_KEY") ?? string.Empty
            : AI_DevsApiKey;
}
