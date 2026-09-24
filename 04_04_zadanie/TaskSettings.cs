namespace _04_04_zadanie;

/// <summary>
/// Everything about the task that is not a provider setting, bound from the "Filesystem" section.
/// The cities, people and goods are deliberately absent: they are read from Natan's notes at
/// runtime, so nothing in the configuration can outvote what the notes actually say.
/// </summary>
public class TaskSettings
{
    public string HubBaseUrl { get; set; } = "https://hub.ag3nts.org";

    /// <summary>Natan's notes, extracted from natan_notes.zip. Resolved against the application directory.</summary>
    public string NotesDirectory { get; set; } = "natan-notes";

    /// <summary>The map of content and the note templates the agent reads before filing anything.</summary>
    public string WorkspaceDirectory { get; set; } = "workspace";

    public string CacheDirectory { get; set; } = "filesystem-cache";

    public int MaxIterations { get; set; } = 40;

    /// <summary>How many calls the whole run may make to /verify. The batch submission is one of them.</summary>
    public int MaxHubRequests { get; set; } = 10;

    public double MinSecondsBetweenHubRequests { get; set; } = 1.0;
}
