namespace _03_01_zadanie;

/// <summary>
/// Everything about the task that is not a provider setting, bound from the "Sensors" section.
/// </summary>
public class TaskSettings
{
    public string HubBaseUrl { get; set; } = "https://hub.ag3nts.org";
    public string ArchiveUrl { get; set; } = "https://hub.ag3nts.org/dane/sensors.zip";
    public string CacheDirectory { get; set; } = "sensors-cache";

    /// <summary>How many statements travel in one classification request.</summary>
    public int BatchSize { get; set; } = 60;

    public int MaxParallelBatches { get; set; } = 4;

    /// <summary>
    /// Score the note classifier must reach on the labelled dataset before an answer is built.
    /// </summary>
    public double MinAcceptableEvalAccuracy { get; set; } = 0.95;
}
