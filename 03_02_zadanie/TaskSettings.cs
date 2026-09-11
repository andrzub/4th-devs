namespace _03_02_zadanie;

/// <summary>
/// Everything about the task that is not a provider setting, bound from the "Firmware" section.
/// </summary>
public class TaskSettings
{
    public string HubBaseUrl { get; set; } = "https://hub.ag3nts.org";

    /// <summary>The cooling software that has to be made to start.</summary>
    public string BinaryPath { get; set; } = "/opt/firmware/cooler/cooler.bin";

    public string CacheDirectory { get; set; } = "firmware-cache";

    /// <summary>
    /// Directories the task puts out of bounds, from configuration. Left empty by default because
    /// the configuration binder appends to an array it finds already populated, which would list
    /// every entry twice.
    /// </summary>
    public string[] ForbiddenPaths { get; set; } = [];

    /// <summary>
    /// The blacklist actually handed to the guard: the task's own list stands in when configuration
    /// says nothing, so a deleted setting cannot quietly disarm the guard.
    /// </summary>
    public IReadOnlyList<string> EffectiveForbiddenPaths =>
        ForbiddenPaths.Length > 0 ? [.. ForbiddenPaths.Distinct()] : ["/etc", "/root", "/proc"];

    public int MaxIterations { get; set; } = 40;

    /// <summary>How many commands the whole run may send to the machine, recon included.</summary>
    public int MaxShellRequests { get; set; } = 80;

    public int MaxHubSubmissions { get; set; } = 5;

    public int MaxReboots { get; set; } = 2;

    /// <summary>Cap on how much of one command's output travels back into the model's context.</summary>
    public int MaxOutputCharacters { get; set; } = 4000;
}
