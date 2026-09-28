namespace _05_01_zadanie;

/// <summary>Everything about the task that is not a provider setting, bound from the "Radiomonitoring" section.</summary>
public class TaskSettings
{
    public string HubBaseUrl { get; set; } = "https://hub.ag3nts.org";

    public string CacheDirectory { get; set; } = "radiomonitoring-cache";

    /// <summary>Safety cap on listen calls in one collection run, in case the hub never says the material is exhausted.</summary>
    public int MaxListens { get; set; } = 200;

    public double MinSecondsBetweenHubRequests { get; set; } = 0.5;

    /// <summary>Images are downscaled to this longest edge before a vision model sees them.</summary>
    public int ImageMaxEdgePixels { get; set; } = 1600;

    /// <summary>Text attachments above this size are not pasted into the analyst's prompt.</summary>
    public int MaxInlineDocumentBytes { get; set; } = 20000;
}
