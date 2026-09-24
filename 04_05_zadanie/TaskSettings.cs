namespace _04_05_zadanie;

/// <summary>
/// Everything about the task that is not a provider setting, bound from the "Foodwarehouse" section.
/// Destination codes and creators are deliberately absent: the agent reads them out of the database
/// at runtime, so nothing in the configuration can outvote what the database actually says.
/// </summary>
public class TaskSettings
{
    public string HubBaseUrl { get; set; } = "https://hub.ag3nts.org";

    /// <summary>Where the hub publishes what each city needs.</summary>
    public string DemandUrl { get; set; } = "https://hub.ag3nts.org/dane/food4cities.json";

    /// <summary>The local copy of the demand file, read by every mode; refreshed with --fetch.</summary>
    public string DemandFile { get; set; } = "data/food4cities.json";

    public string CacheDirectory { get; set; } = "foodwarehouse-cache";

    public int MaxIterations { get; set; } = 30;

    /// <summary>How many calls one process may make to /verify. Exploration and execution both count.</summary>
    public int MaxHubRequests { get; set; } = 60;

    public double MinSecondsBetweenHubRequests { get; set; } = 1.0;
}
