namespace _04_02_zadanie;

/// <summary>
/// Everything about the task that is not a provider setting, bound from the "WindPower" section.
/// The turbine's limits, the plant's deficit and the shape of the API are absent on purpose:
/// they are read at runtime, inside the service window, from whatever the API reports then.
/// </summary>
public class TaskSettings
{
    public string HubBaseUrl { get; set; } = "https://hub.ag3nts.org";

    public string CacheDirectory { get; set; } = "windpower-cache";

    /// <summary>Kept short: inside a service window measured in seconds, a stuck request is a lost run.</summary>
    public int RequestTimeoutSeconds { get; set; } = 20;

    /// <summary>How long the run may work inside the window; the API grants forty seconds and stopping early is free.</summary>
    public int WindowBudgetSeconds { get; set; } = 37;
}
