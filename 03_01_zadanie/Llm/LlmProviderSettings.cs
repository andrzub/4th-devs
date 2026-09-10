namespace _03_01_zadanie.Llm;

/// <summary>
/// Connection settings for one OpenAI-compatible provider, bound from a configuration
/// section named after the role it plays in this task ("Classifier"). Prices are part of the
/// settings because cost accounting is a first-class output here, and tariffs change more
/// often than code.
/// </summary>
public class LlmProviderSettings
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string DefaultModel { get; set; } = string.Empty;

    /// <summary>
    /// Minimum spacing between consecutive requests, in seconds. Zero disables throttling.
    /// </summary>
    public double MinSecondsBetweenRequests { get; set; }

    public decimal InputPricePerMillionTokens { get; set; }

    /// <summary>Discounted price for prompt tokens served from the provider's prefix cache.</summary>
    public decimal CachedInputPricePerMillionTokens { get; set; }

    public decimal OutputPricePerMillionTokens { get; set; }
}
