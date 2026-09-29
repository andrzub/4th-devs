namespace _05_02_zadanie;

/// <summary>Everything about the task that is not a provider setting, bound from the "Phonecall" section.</summary>
public class TaskSettings
{
    public string HubBaseUrl { get; set; } = "https://hub.ag3nts.org";

    public string CacheDirectory { get; set; } = "phonecall-cache";

    /// <summary>The identity the call opens with.</summary>
    public string CallerName { get; set; } = string.Empty;

    /// <summary>
    /// Road codes to ask about. Starts empty on purpose: the configuration binder appends a JSON array
    /// to a non-empty default instead of replacing it.
    /// </summary>
    public List<string> Roads { get; set; } = [];

    /// <summary>The operators' password, given only when the operator asks for it.</summary>
    public string Password { get; set; } = string.Empty;
}
