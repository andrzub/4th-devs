namespace _02_02_zadanie.Llm;

/// <summary>
/// Connection settings for one OpenAI-compatible provider, bound from a configuration
/// section ("OpenAI", "Gemini"). Gemini is consumed through its OpenAI-compatible
/// endpoint, so both providers share the same client class and differ only in settings.
/// </summary>
public class LlmProviderSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = string.Empty;

    /// <summary>
    /// Minimum spacing between consecutive requests, in seconds. Used to stay under
    /// per-minute rate limits (Gemini free tier allows ~10 requests/minute).
    /// Zero disables throttling.
    /// </summary>
    public double MinSecondsBetweenRequests { get; set; }
}
