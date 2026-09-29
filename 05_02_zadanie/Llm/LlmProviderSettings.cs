namespace _05_02_zadanie.Llm;

/// <summary>
/// Connection settings for one OpenAI-compatible chat provider, bound from a configuration
/// section named after its role ("Interpreter").
/// </summary>
public class LlmProviderSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = string.Empty;

    /// <summary>Minimum spacing between consecutive requests, in seconds. Zero disables throttling.</summary>
    public double MinSecondsBetweenRequests { get; set; }
}
