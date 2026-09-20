namespace _04_01_zadanie;

/// <summary>
/// Everything about the task that is not a provider setting, bound from the "OkoEditor" section.
/// The records themselves are deliberately absent: what the console holds, which identifiers
/// address which row and how incidents are classified is read at runtime, so a stale briefing
/// cannot outvote what the console actually shows.
/// </summary>
public class TaskSettings
{
    public string HubBaseUrl { get; set; } = "https://hub.ag3nts.org";

    public string PanelBaseUrl { get; set; } = "https://oko.ag3nts.org";

    public string PanelLogin { get; set; } = "Zofia";

    public string PanelPassword { get; set; } = string.Empty;

    public string CacheDirectory { get; set; } = "okoeditor-cache";

    public int MaxIterations { get; set; } = 30;

    /// <summary>How many calls the whole run may make to /verify. Every edit is one of them.</summary>
    public int MaxHubRequests { get; set; } = 25;

    public double MinSecondsBetweenHubRequests { get; set; } = 1.5;

    /// <summary>The city whose incident is rewritten into the decoy. Orders from the centre, not a discovery.</summary>
    public string DecoyIncidentCity { get; set; } = "Domatowo";

    /// <summary>The uninhabited city the operators are meant to look at instead.</summary>
    public string DecoyTargetCity { get; set; } = "Komarowo";

    /// <summary>The city the whole errand exists to protect.</summary>
    public string ProtectedCity { get; set; } = "Skolwin";
}
