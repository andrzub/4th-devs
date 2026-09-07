namespace _02_04_zadanie.Llm;

/// <summary>
/// Connection settings for one OpenAI-compatible provider, bound from a configuration section.
/// Sections are named by role ("Agent", "Scanner"), so each role can point at a different
/// model or even a different provider without touching code.
/// </summary>
public class LlmProviderSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = string.Empty;

    /// <summary>
    /// Minimum spacing between consecutive requests, in seconds, for providers with tight
    /// per-minute limits (e.g. Gemini free tier). Zero disables throttling.
    /// </summary>
    public double MinSecondsBetweenRequests { get; set; }
}
